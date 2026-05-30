using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Paint.Interop;
using Rect = Starling.Layout.Rect;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuExt = Silk.NET.WebGPU.Extensions.WGPU.Wgpu;

namespace Starling.Paint.Compositor;

/// <summary>
/// One layer ready to blend into the viewport: the layer's own bitmap (over a
/// transparent canvas), the content-hash key that lets the GPU keep its texture
/// resident across frames, the affine map from a layer-bitmap pixel to an output
/// device pixel, the effective opacity, and an optional device-space clip rect.
/// The <see cref="Compositor"/> builds these once and hands them to either the
/// CPU blend loop or <see cref="GpuLayerCompositor"/>, so both paths share the
/// exact same geometry.
/// </summary>
internal readonly struct LayerBlend(
    RenderedBitmap local,
    long contentHash,
    Matrix2D localToDevice,
    float opacity,
    Rect? clipDevice)
{
    public RenderedBitmap Local { get; } = local;
    public long ContentHash { get; } = contentHash;
    public Matrix2D LocalToDevice { get; } = localToDevice;
    public float Opacity { get; } = opacity;
    public Rect? ClipDevice { get; } = clipDevice;
}

/// <summary>
/// Blends a list of cached layer bitmaps into the viewport on the GPU
/// (wp:M12-13-gpu-composite-blend). Each layer uploads to a wgpu texture once,
/// keyed by its slice content hash, and stays resident across frames — an
/// unchanged layer is never re-uploaded. Every frame the layers blend in paint
/// order in a single render pass: a textured quad per layer, positioned by the
/// layer's transform, scaled by its opacity, and clipped by a scissor rect. The
/// blend is alpha-over in premultiplied space, which reproduces the CPU
/// <see cref="Compositor"/>'s <c>AlphaOver</c> math (the framebuffer base is
/// opaque white, so premultiplied and straight alpha agree on readback).
/// </summary>
/// <remarks>
/// This replaces the O(viewport pixels) per-layer CPU blend. The device, queue,
/// render pipeline, and sampler are created once and reused; the only per-frame
/// GPU→CPU transfer is the final viewport readback, the same transfer the flat
/// raster path already pays. <see cref="TryCreate"/> returns <c>null</c> when no
/// GPU adapter is available, and the compositor falls back to the CPU blend.
/// </remarks>
internal sealed unsafe class GpuLayerCompositor : IDisposable
{
    // One process-wide instance: device acquisition is expensive and there is
    // exactly one paint thread. Lazily probed; null when the host has no adapter.
    private static readonly Lazy<GpuLayerCompositor?> _shared = new(TryCreate);
    internal static GpuLayerCompositor? Shared => _shared.Value;

    private readonly WebGPU _api;
    private readonly WgpuExt? _poll;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly RenderPipeline* _pipeline;
    private readonly BindGroupLayout* _bindLayout;
    private readonly Sampler* _sampler;
    private readonly object _gate = new();

    // Per-layer GPU textures, keyed by slice content hash. Resident across frames
    // so an unchanged layer never re-uploads (the LTF caches already prove the
    // layer's CPU bitmap is unchanged; the hash is its identity).
    private readonly Dictionary<long, CachedTexture> _textures = new();
    private ulong _frame;
    // Drop a texture after this many frames untouched, so an animation that walks
    // through many distinct slices (e.g. a counter) doesn't grow GPU memory
    // without bound. 240 frames ≈ 4 s at 60 fps — long enough that an alternating
    // two-state layer stays hot.
    private const ulong EvictAfterFrames = 240;

    // Output render target + readback buffer + vertex buffer, recreated when the
    // viewport size or the layer count grows.
    private Texture* _outTex;
    private TextureView* _outView;
    private int _outW, _outH;
    private WgpuBuffer* _readback;
    private nuint _readbackSize;
    private WgpuBuffer* _vertexBuffer;
    private nuint _vertexCapacity;

    // 5 floats per vertex (ndc.x, ndc.y, u, v, opacity), 6 vertices per layer quad.
    private const int FloatsPerVertex = 5;
    private const int VertsPerQuad = 6;
    private const uint VertexStride = FloatsPerVertex * sizeof(float);

    private struct CachedTexture
    {
        public nint Texture;
        public nint View;
        public nint BindGroup;
        public int Width, Height;
        public ulong LastFrame;
    }

    private GpuLayerCompositor(WebGPU api, WgpuExt? poll, Device* device, Queue* queue,
        RenderPipeline* pipeline, BindGroupLayout* bindLayout, Sampler* sampler)
    {
        _api = api;
        _poll = poll;
        _device = device;
        _queue = queue;
        _pipeline = pipeline;
        _bindLayout = bindLayout;
        _sampler = sampler;
    }

    /// <summary>
    /// Probes for a GPU adapter and builds the device/pipeline once. Returns
    /// <c>null</c> on any failure so the caller can fall back to the CPU blend —
    /// a host with no GPU (CI, a sandbox) must still composite.
    /// </summary>
    internal static GpuLayerCompositor? TryCreate()
    {
        try
        {
            var api = WebGPU.GetApi();

            var instance = api.CreateInstance((InstanceDescriptor*)null);
            if (instance == null) return null;

            Adapter* adapter = null;
            var adapterOpts = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
            var adapterCb = PfnRequestAdapterCallback.From((status, a, _, _) =>
            {
                if (status == RequestAdapterStatus.Success) adapter = a;
            });
            api.InstanceRequestAdapter(instance, in adapterOpts, adapterCb, null);
            if (adapter == null) { api.InstanceRelease(instance); return null; }

            Device* device = null;
            var deviceDesc = default(DeviceDescriptor);
            var deviceCb = PfnRequestDeviceCallback.From((status, d, _, _) =>
            {
                if (status == RequestDeviceStatus.Success) device = d;
            });
            api.AdapterRequestDevice(adapter, in deviceDesc, deviceCb, null);
            api.AdapterRelease(adapter);
            api.InstanceRelease(instance);
            if (device == null) return null;

            var queue = api.DeviceGetQueue(device);
            if (queue == null) { api.DeviceRelease(device); return null; }

            // Swallow uncaptured device errors instead of letting wgpu's default
            // handler abort() the process (the same abort the ImageSharp backend
            // guards against). A failed frame falls back to CPU.
            var errorCb = PfnErrorCallback.From((_, _, _) => { });
            api.DeviceSetUncapturedErrorCallback(device, errorCb, null);

            WgpuExt? poll = null;
            try { poll = new WgpuExt(api.Context); } catch { poll = null; }

            if (!BuildPipeline(api, device, out var pipeline, out var bindLayout, out var sampler))
            {
                api.QueueRelease(queue);
                api.DeviceRelease(device);
                return null;
            }

            return new GpuLayerCompositor(api, poll, device, queue, pipeline, bindLayout, sampler);
        }
        catch
        {
            // Missing wgpu binary, no adapter, or a binding mismatch: fall back to CPU.
            _ = WgpuNativeLoader.Diagnose();
            return null;
        }
    }

    private static bool BuildPipeline(WebGPU api, Device* device,
        out RenderPipeline* pipeline, out BindGroupLayout* bindLayout, out Sampler* sampler)
    {
        pipeline = null;
        bindLayout = null;
        sampler = null;

        var codePtr = (byte*)SilkMarshal.StringToPtr(ShaderSource, NativeStringEncoding.UTF8);
        var vsEntry = (byte*)SilkMarshal.StringToPtr("vs_main", NativeStringEncoding.UTF8);
        var fsEntry = (byte*)SilkMarshal.StringToPtr("fs_main", NativeStringEncoding.UTF8);
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = codePtr,
            };
            var shaderDesc = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
            var module = api.DeviceCreateShaderModule(device, in shaderDesc);
            if (module == null) return false;

            // Vertex layout: float32x2 position, float32x2 uv, float32 opacity.
            var attrs = stackalloc VertexAttribute[3];
            attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
            attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 2 * sizeof(float), ShaderLocation = 1 };
            attrs[2] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 4 * sizeof(float), ShaderLocation = 2 };
            var vbl = new VertexBufferLayout
            {
                ArrayStride = VertexStride,
                StepMode = VertexStepMode.Vertex,
                AttributeCount = 3,
                Attributes = attrs,
            };

            // Premultiplied alpha-over: out = src + dst*(1-srcA). The fragment
            // shader outputs premultiplied colour, so this matches the CPU
            // AlphaOver exactly when the framebuffer base is opaque.
            var blend = new BlendState
            {
                Color = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
                Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            };
            var colorTarget = new ColorTargetState
            {
                Format = TextureFormat.Rgba8Unorm,
                Blend = &blend,
                WriteMask = ColorWriteMask.All,
            };
            var fragment = new FragmentState
            {
                Module = module,
                EntryPoint = fsEntry,
                TargetCount = 1,
                Targets = &colorTarget,
            };
            var pipelineDesc = new RenderPipelineDescriptor
            {
                Layout = null, // auto layout from the shader's bind group
                Vertex = new VertexState
                {
                    Module = module,
                    EntryPoint = vsEntry,
                    BufferCount = 1,
                    Buffers = &vbl,
                },
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None,
                },
                Multisample = new MultisampleState { Count = 1, Mask = ~0u, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            pipeline = api.DeviceCreateRenderPipeline(device, in pipelineDesc);
            api.ShaderModuleRelease(module);
            if (pipeline == null) return false;

            bindLayout = api.RenderPipelineGetBindGroupLayout(pipeline, 0);

            // Linear sampler so a rotated/scaled layer filters like the CPU
            // bilinear sample; at integer alignment it returns the exact texel.
            var samplerDesc = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Nearest,
                LodMinClamp = 0,
                LodMaxClamp = 1,
                MaxAnisotropy = 1,
            };
            sampler = api.DeviceCreateSampler(device, in samplerDesc);
            return sampler != null;
        }
        finally
        {
            SilkMarshal.Free((nint)codePtr);
            SilkMarshal.Free((nint)vsEntry);
            SilkMarshal.Free((nint)fsEntry);
        }
    }

    /// <summary>
    /// Blends <paramref name="ops"/> into <paramref name="output"/> (a
    /// width×height straight-alpha RGBA8 buffer pre-filled opaque white) on the
    /// GPU. Returns <c>false</c> on any GPU failure so the caller can fall back
    /// to the CPU blend for this frame.
    /// </summary>
    public bool Composite(byte[] output, int width, int height, IReadOnlyList<LayerBlend> ops)
    {
        if (width <= 0 || height <= 0) return false;
        lock (_gate)
        {
            try
            {
                _frame++;
                EnsureTarget(width, height);
                UploadLayerTextures(ops);
                var vertexCount = BuildAndUploadVertices(ops, width, height);
                RenderPass(width, height, ops, vertexCount);
                Readback(output, width, height);
                EvictStaleTextures();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private void EnsureTarget(int width, int height)
    {
        if (_outTex != null && _outW == width && _outH == height) return;

        if (_outView != null) { _api.TextureViewRelease(_outView); _outView = null; }
        if (_outTex != null) { _api.TextureRelease(_outTex); _outTex = null; }
        if (_readback != null) { _api.BufferRelease(_readback); _readback = null; }

        var desc = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _outTex = _api.DeviceCreateTexture(_device, in desc);
        _outView = _api.TextureCreateView(_outTex, (TextureViewDescriptor*)null);
        _outW = width;
        _outH = height;

        var padded = Align256((uint)(width * 4));
        _readbackSize = (nuint)((ulong)padded * (ulong)height);
        var bufDesc = new BufferDescriptor
        {
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
            Size = _readbackSize,
            MappedAtCreation = false,
        };
        _readback = _api.DeviceCreateBuffer(_device, in bufDesc);
    }

    private void UploadLayerTextures(IReadOnlyList<LayerBlend> ops)
    {
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var bmp = op.Local;
            if (_textures.TryGetValue(op.ContentHash, out var cached)
                && cached.Width == bmp.Width && cached.Height == bmp.Height)
            {
                cached.LastFrame = _frame;
                _textures[op.ContentHash] = cached;
                continue; // resident — no re-upload
            }

            if (cached.Texture != 0) ReleaseCached(cached); // stale dims, replace

            var (texPtr, viewPtr, bgPtr) = CreateAndUpload(bmp);
            _textures[op.ContentHash] = new CachedTexture
            {
                Texture = texPtr,
                View = viewPtr,
                BindGroup = bgPtr,
                Width = bmp.Width,
                Height = bmp.Height,
                LastFrame = _frame,
            };
        }
    }

    private (nint Texture, nint View, nint BindGroup) CreateAndUpload(RenderedBitmap bmp)
    {
        var desc = new TextureDescriptor
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D { Width = (uint)bmp.Width, Height = (uint)bmp.Height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        var tex = _api.DeviceCreateTexture(_device, in desc);
        var view = _api.TextureCreateView(tex, (TextureViewDescriptor*)null);

        // Upload premultiplied pixels so the linear sampler filters premultiplied
        // colour — matching the CPU Sample(), which premultiplies before
        // interpolating so a transparent neighbour never leaks colour at an edge.
        var premul = Premultiply(bmp);
        var bytesPerRow = (uint)(bmp.Width * 4);
        var copyTex = new ImageCopyTexture
        {
            Texture = tex,
            MipLevel = 0,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All,
        };
        var layout = new TextureDataLayout { Offset = 0, BytesPerRow = bytesPerRow, RowsPerImage = (uint)bmp.Height };
        var extent = new Extent3D { Width = (uint)bmp.Width, Height = (uint)bmp.Height, DepthOrArrayLayers = 1 };
        fixed (byte* p = premul)
            _api.QueueWriteTexture(_queue, in copyTex, p, (nuint)premul.Length, in layout, in extent);

        // Bind group: texture view + shared sampler.
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = view };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };
        var bgDesc = new BindGroupDescriptor { Layout = _bindLayout, EntryCount = 2, Entries = entries };
        var bg = _api.DeviceCreateBindGroup(_device, in bgDesc);

        return ((nint)tex, (nint)view, (nint)bg);
    }

    private static byte[] Premultiply(RenderedBitmap bmp)
    {
        var src = bmp.Rgba;
        var dst = new byte[src.Length];
        for (var i = 0; i < src.Length; i += 4)
        {
            var a = src[i + 3];
            if (a == 255)
            {
                dst[i] = src[i];
                dst[i + 1] = src[i + 1];
                dst[i + 2] = src[i + 2];
                dst[i + 3] = 255;
            }
            else if (a == 0)
            {
                // Leave fully transparent texels at zero (premultiplied 0).
            }
            else
            {
                dst[i] = (byte)((src[i] * a + 127) / 255);
                dst[i + 1] = (byte)((src[i + 1] * a + 127) / 255);
                dst[i + 2] = (byte)((src[i + 2] * a + 127) / 255);
                dst[i + 3] = a;
            }
        }
        return dst;
    }

    private uint BuildAndUploadVertices(IReadOnlyList<LayerBlend> ops, int width, int height)
    {
        var totalVerts = ops.Count * VertsPerQuad;
        var verts = new float[totalVerts * FloatsPerVertex];
        var f = 0;
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var w = op.Local.Width;
            var h = op.Local.Height;
            var m = op.LocalToDevice;

            // Local-bitmap corners -> device pixels -> clip-space (NDC). Device y
            // grows downward; NDC y grows upward, so flip y.
            var c0 = Corner(m, 0, 0, 0, 0, width, height);
            var c1 = Corner(m, w, 0, 1, 0, width, height);
            var c2 = Corner(m, w, h, 1, 1, width, height);
            var c3 = Corner(m, 0, h, 0, 1, width, height);

            var op4 = op.Opacity;
            // Two triangles: 0-1-2, 0-2-3.
            Emit(verts, ref f, c0, op4);
            Emit(verts, ref f, c1, op4);
            Emit(verts, ref f, c2, op4);
            Emit(verts, ref f, c0, op4);
            Emit(verts, ref f, c2, op4);
            Emit(verts, ref f, c3, op4);
        }

        var byteLen = (nuint)(verts.Length * sizeof(float));
        EnsureVertexBuffer(byteLen);
        if (byteLen > 0)
        {
            fixed (float* p = verts)
                _api.QueueWriteBuffer(_queue, _vertexBuffer, 0, p, byteLen);
        }
        return (uint)totalVerts;
    }

    private static (float, float, float, float) Corner(Matrix2D m, double lx, double ly, float u, float v, int width, int height)
    {
        var (dx, dy) = m.Transform(lx, ly);
        var nx = (float)(dx / width * 2.0 - 1.0);
        var ny = (float)(1.0 - dy / height * 2.0);
        return (nx, ny, u, v);
    }

    private static void Emit(float[] verts, ref int f, (float nx, float ny, float u, float v) c, float opacity)
    {
        verts[f++] = c.nx;
        verts[f++] = c.ny;
        verts[f++] = c.u;
        verts[f++] = c.v;
        verts[f++] = opacity;
    }

    private void EnsureVertexBuffer(nuint byteLen)
    {
        if (byteLen == 0) return;
        if (_vertexBuffer != null && _vertexCapacity >= byteLen) return;
        if (_vertexBuffer != null) { _api.BufferRelease(_vertexBuffer); _vertexBuffer = null; }
        // Round up so a frame with one more layer doesn't reallocate every time.
        var cap = (nuint)Align256((uint)byteLen);
        var desc = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = cap, MappedAtCreation = false };
        _vertexBuffer = _api.DeviceCreateBuffer(_device, in desc);
        _vertexCapacity = cap;
    }

    private void RenderPass(int width, int height, IReadOnlyList<LayerBlend> ops, uint vertexCount)
    {
        var encoder = _api.DeviceCreateCommandEncoder(_device, (CommandEncoderDescriptor*)null);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _outView,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            // Opaque white base — the page background the flat path also clears
            // to, and what FillWhite establishes for the CPU blend.
            ClearValue = new Color { R = 1, G = 1, B = 1, A = 1 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };
        var pass = _api.CommandEncoderBeginRenderPass(encoder, in passDesc);

        _api.RenderPassEncoderSetPipeline(pass, _pipeline);
        if (vertexCount > 0)
            _api.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, _vertexCapacity);

        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            if (!_textures.TryGetValue(op.ContentHash, out var tex)) continue;

            SetScissor(pass, op.ClipDevice, width, height);
            _api.RenderPassEncoderSetBindGroup(pass, 0, (BindGroup*)tex.BindGroup, 0, (uint*)null);
            _api.RenderPassEncoderDraw(pass, VertsPerQuad, 1, (uint)(i * VertsPerQuad), 0);
        }

        _api.RenderPassEncoderEnd(pass);

        // Copy the output texture into the readback buffer (256-byte row pitch).
        var padded = Align256((uint)(width * 4));
        var src = new ImageCopyTexture
        {
            Texture = _outTex,
            MipLevel = 0,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All,
        };
        var dst = new ImageCopyBuffer
        {
            Buffer = _readback,
            Layout = new TextureDataLayout { Offset = 0, BytesPerRow = padded, RowsPerImage = (uint)height },
        };
        var extent = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
        _api.CommandEncoderCopyTextureToBuffer(encoder, in src, in dst, in extent);

        var cmd = _api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
        _api.QueueSubmit(_queue, 1, &cmd);
        _api.CommandBufferRelease(cmd);
        _api.CommandEncoderRelease(encoder);
        _api.RenderPassEncoderRelease(pass);
    }

    private void SetScissor(RenderPassEncoder* pass, Rect? clip, int width, int height)
    {
        int x = 0, y = 0, w = width, h = height;
        if (clip is { } cd)
        {
            var minX = Math.Max(0, (int)Math.Floor(cd.X));
            var minY = Math.Max(0, (int)Math.Floor(cd.Y));
            var maxX = Math.Min(width, (int)Math.Ceiling(cd.Right));
            var maxY = Math.Min(height, (int)Math.Ceiling(cd.Bottom));
            x = minX; y = minY;
            w = Math.Max(0, maxX - minX);
            h = Math.Max(0, maxY - minY);
        }
        _api.RenderPassEncoderSetScissorRect(pass, (uint)x, (uint)y, (uint)w, (uint)h);
    }

    private void Readback(byte[] output, int width, int height)
    {
        var mapped = false;
        var mapReady = new ManualResetEventSlim(false);
        var status = BufferMapAsyncStatus.Unknown;
        var cb = PfnBufferMapCallback.From((s, _) => { status = s; mapReady.Set(); });
        try
        {
            _api.BufferMapAsync(_readback, MapMode.Read, 0, _readbackSize, cb, null);
            if (!WaitForMap(mapReady) || status != BufferMapAsyncStatus.Success)
                throw new InvalidOperationException($"WebGPU readback map failed: {status}");
            mapped = true;

            var padded = (int)Align256((uint)(width * 4));
            var rowBytes = width * 4;
            var src = (byte*)_api.BufferGetConstMappedRange(_readback, 0, _readbackSize);
            if (src == null) throw new InvalidOperationException("WebGPU readback returned no data.");

            var srcSpan = new ReadOnlySpan<byte>(src, (int)_readbackSize);
            for (var row = 0; row < height; row++)
                srcSpan.Slice(row * padded, rowBytes).CopyTo(output.AsSpan(row * rowBytes, rowBytes));
        }
        finally
        {
            if (mapped) _api.BufferUnmap(_readback);
            ((IDisposable)cb).Dispose();
            mapReady.Dispose();
        }
    }

    private bool WaitForMap(ManualResetEventSlim signal)
    {
        if (_poll is null) return signal.Wait(5000);
        var deadline = Environment.TickCount64 + 5000;
        while (!signal.IsSet && Environment.TickCount64 < deadline)
            _poll.DevicePoll(_device, true, (Silk.NET.WebGPU.Extensions.WGPU.WrappedSubmissionIndex*)null);
        return signal.IsSet;
    }

    private void EvictStaleTextures()
    {
        if (_textures.Count == 0) return;
        List<long>? drop = null;
        foreach (var kv in _textures)
        {
            if (_frame - kv.Value.LastFrame > EvictAfterFrames)
                (drop ??= new List<long>()).Add(kv.Key);
        }
        if (drop is null) return;
        foreach (var key in drop)
        {
            ReleaseCached(_textures[key]);
            _textures.Remove(key);
        }
    }

    private void ReleaseCached(CachedTexture c)
    {
        if (c.BindGroup != 0) _api.BindGroupRelease((BindGroup*)c.BindGroup);
        if (c.View != 0) _api.TextureViewRelease((TextureView*)c.View);
        if (c.Texture != 0) _api.TextureRelease((Texture*)c.Texture);
    }

    private static uint Align256(uint value) => (value + 255u) & ~255u;

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var c in _textures.Values) ReleaseCached(c);
            _textures.Clear();
            if (_vertexBuffer != null) { _api.BufferRelease(_vertexBuffer); _vertexBuffer = null; }
            if (_readback != null) { _api.BufferRelease(_readback); _readback = null; }
            if (_outView != null) { _api.TextureViewRelease(_outView); _outView = null; }
            if (_outTex != null) { _api.TextureRelease(_outTex); _outTex = null; }
            if (_sampler != null) _api.SamplerRelease(_sampler);
            if (_pipeline != null) _api.RenderPipelineRelease(_pipeline);
            if (_queue != null) _api.QueueRelease(_queue);
            if (_device != null) _api.DeviceRelease(_device);
        }
    }

    // Textures store premultiplied straight-RGBA8 (no sRGB), so the blend runs in
    // the same gamma space as the CPU AlphaOver. The fragment scales the
    // premultiplied sample by the layer opacity; the One/OneMinusSrcAlpha blend
    // then composites it over the framebuffer.
    private const string ShaderSource = @"
struct VsOut {
    @builtin(position) pos : vec4<f32>,
    @location(0) uv : vec2<f32>,
    @location(1) opacity : f32,
};

@vertex
fn vs_main(@location(0) pos : vec2<f32>, @location(1) uv : vec2<f32>, @location(2) opacity : f32) -> VsOut {
    var o : VsOut;
    o.pos = vec4<f32>(pos, 0.0, 1.0);
    o.uv = uv;
    o.opacity = opacity;
    return o;
}

@group(0) @binding(0) var tex : texture_2d<f32>;
@group(0) @binding(1) var smp : sampler;

@fragment
fn fs_main(in : VsOut) -> @location(0) vec4<f32> {
    let c = textureSample(tex, smp, in.uv); // premultiplied
    return c * in.opacity;
}
";
}
