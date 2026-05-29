using AwesomeAssertions;

namespace Starling.Bindings.Tests;

[TestClass]
public sealed class CoreWebApiTests
{
    [TestMethod]
    public void Url_and_search_params_follow_browser_shape()
    {
        var env = FetchTests.NewEnv("https://example.test/base/index.html");
        FetchTests.Eval(env.Runtime, @"
            var u = new URL('../api?b=2', 'https://example.test/base/index.html');
            u.searchParams.append('a', 'hello world');
            u.searchParams.set('b', '3');
            globalThis.href = u.href;
            globalThis.param = u.searchParams.get('a');
            u.href = 'https://example.test/next?fresh=1';
            globalThis.changedParam = u.searchParams.get('fresh') + ':' + u.searchParams.has('a');
            globalThis.serialized = new URLSearchParams({ q: 'star ling', n: 1 }).toString();
        ");

        env.Runtime.GetGlobal("href").AsString.Should().Be("https://example.test/api?b=3&a=hello+world");
        env.Runtime.GetGlobal("param").AsString.Should().Be("hello world");
        env.Runtime.GetGlobal("changedParam").AsString.Should().Be("1:false");
        env.Runtime.GetGlobal("serialized").AsString.Should().Be("q=star+ling&n=1");
    }

    [TestMethod]
    public async Task Blob_file_and_response_body_readers_work()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, @"
            globalThis.blobText = null;
            globalThis.fileName = null;
            globalThis.fileType = null;
            globalThis.responseBlobType = null;
            var blob = new Blob(['hello'], { type: 'text/plain' });
            var file = new File(['data'], 'note.txt', { type: 'text/plain', lastModified: 123 });
            blob.text().then(function(t) {
                globalThis.blobText = t;
                globalThis.fileName = file.name + ':' + file.lastModified;
                globalThis.fileType = file.type;
                return new Response(blob).blob();
            }).then(function(b) {
                globalThis.responseBlobType = b.type + ':' + b.size;
            });
        ");

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("responseBlobType").IsString);
        env.Runtime.GetGlobal("blobText").AsString.Should().Be("hello");
        env.Runtime.GetGlobal("fileName").AsString.Should().Be("note.txt:123");
        env.Runtime.GetGlobal("fileType").AsString.Should().Be("text/plain");
        env.Runtime.GetGlobal("responseBlobType").AsString.Should().Be("text/plain:5");
    }

    [TestMethod]
    public async Task FormData_and_response_formData_use_url_encoded_entries()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, @"
            globalThis.first = null;
            globalThis.all = null;
            globalThis.parsed = null;
            var fd = new FormData();
            fd.append('a', '1');
            fd.append('a', '2');
            globalThis.first = fd.get('a');
            globalThis.all = fd.getAll('a').length;
            var r = new Response('x=one&y=two+words', {
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
            });
            r.formData().then(function(form) {
                globalThis.parsed = form.get('x') + ':' + form.get('y');
            });
        ");

        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("parsed").IsString);
        env.Runtime.GetGlobal("first").AsString.Should().Be("1");
        env.Runtime.GetGlobal("all").AsNumber.Should().Be(2);
        env.Runtime.GetGlobal("parsed").AsString.Should().Be("one:two words");
    }

    [TestMethod]
    public void Text_encoding_base64_structuredClone_and_dom_exception_are_available()
    {
        var env = FetchTests.NewEnv("https://example.test/");
        FetchTests.Eval(env.Runtime, @"
            var bytes = new TextEncoder().encode('hi');
            globalThis.decoded = new TextDecoder().decode(bytes);
            var bomBytes = new Uint8Array([239, 187, 191, 65]);
            globalThis.bom = new TextDecoder().decode(bomBytes) + ':' + new TextDecoder('utf-8', { ignoreBOM: true }).decode(bomBytes).length;
            globalThis.byteInfo = bytes.length + ':' + bytes[0] + ':' + bytes[1];
            globalThis.b64 = btoa('Starling');
            globalThis.unpadded = atob('YQ');
            globalThis.plain = atob(globalThis.b64);
            var source = { a: 1 };
            source.self = source;
            var copy = structuredClone(source);
            globalThis.cloneOk = copy !== source && copy.self === copy && copy.a === 1;
            var ex = new DOMException('nope', 'DataCloneError');
            globalThis.dom = ex.name + ':' + ex.message + ':' + ex.code + ':' + DOMException.DATA_CLONE_ERR;
        ");

        env.Runtime.GetGlobal("decoded").AsString.Should().Be("hi");
        env.Runtime.GetGlobal("bom").AsString.Should().Be("A:2");
        env.Runtime.GetGlobal("byteInfo").AsString.Should().Be("2:104:105");
        env.Runtime.GetGlobal("b64").AsString.Should().Be("U3Rhcmxpbmc=");
        env.Runtime.GetGlobal("unpadded").AsString.Should().Be("a");
        env.Runtime.GetGlobal("plain").AsString.Should().Be("Starling");
        env.Runtime.GetGlobal("cloneOk").AsBool.Should().BeTrue();
        env.Runtime.GetGlobal("dom").AsString.Should().Be("DataCloneError:nope:25:25");
    }
}
