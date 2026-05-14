# native/shim/CMakeLists.txt — builds libtessera_skia, the single native file
# .NET loads. Statically links the custom C ABI shim against libskia + Dawn.
#
# WP M3-06g (Phase 2).
# --------------------
# Builds the real shim: tessera_skia.cpp wires Dawn + Skia Graphite behind the
# C ABI and statically links libskia + Dawn + their deps into one
# libtessera_skia.{dylib,dll,so} per RID. .NET (WP M3-06h) loads exactly that
# one native file.
#
# Header sourcing — IMPORTANT:
#   Skia's public headers self-reference as `#include "include/core/..."`, so
#   the Skia include ROOT must be the directory that *contains* `include/`.
#   native/build-skia.* stages the *contents* of Skia's include/ into
#   runtimes/<rid>/native/include/skia/, which does NOT satisfy that layout,
#   and it does not stage Dawn's *generated* headers (dawn/webgpu_cpp.h,
#   dawn/dawn_proc_table.h) at all. So for headers this build uses the Skia
#   source checkout under third_party/skia/ (which has include/, plus
#   third_party/externals/dawn/include) and the Dawn gen dir under
#   native/out/<rid>/gen/. The STATIC LIBS still come from the staged
#   runtimes/<rid>/native/ payload. (06b's staging packages libs + a flattened
#   header tree; the shim build needs the source-tree header layout.)
#
# Configure (osx-arm64):
#   cmake -S native/shim -B native/build/osx-arm64 -DCMAKE_BUILD_TYPE=Release
#   cmake --build native/build/osx-arm64

cmake_minimum_required(VERSION 3.20)
project(tessera_skia LANGUAGES C CXX OBJC OBJCXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# --- options ----------------------------------------------------------------
# Scaffold escape hatch (WP M3-06a): build stub bodies with no Skia/Dawn link.
# WP M3-06g default is OFF — a real Skia/Dawn-linked shim.
option(TESSERA_SKIA_STUB_ONLY "Build the shim without linking Skia/Dawn (scaffold mode)" OFF)

# --- resolve the .NET RID + the staged Skia/Dawn payload --------------------
if(NOT DEFINED TESSERA_RID)
  if(APPLE)
    if(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
      set(TESSERA_RID "osx-arm64")
    else()
      set(TESSERA_RID "osx-x64")
    endif()
  elseif(WIN32)
    set(TESSERA_RID "win-x64")
  elseif(UNIX)
    set(TESSERA_RID "linux-x64")
  else()
    message(FATAL_ERROR "Cannot infer TESSERA_RID for this platform; pass -DTESSERA_RID=...")
  endif()
endif()
message(STATUS "tessera_skia: target RID = ${TESSERA_RID}")

get_filename_component(TESSERA_REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
set(TESSERA_NATIVE_STAGE "${TESSERA_REPO_ROOT}/runtimes/${TESSERA_RID}/native")
set(TESSERA_SKIA_SRC     "${TESSERA_REPO_ROOT}/third_party/skia")
set(TESSERA_DAWN_SRC     "${TESSERA_SKIA_SRC}/third_party/externals/dawn")
set(TESSERA_DAWN_GEN     "${TESSERA_REPO_ROOT}/native/out/${TESSERA_RID}/gen/third_party/externals/dawn")

# --- the shim shared library ------------------------------------------------
add_library(tessera_skia SHARED
  tessera_skia.cpp
  tessera_skia.h
)

target_include_directories(tessera_skia PRIVATE
  ${CMAKE_CURRENT_SOURCE_DIR}
)

# Force a consistent output name: libtessera_skia.{dylib,so} / tessera_skia.dll.
set_target_properties(tessera_skia PROPERTIES
  OUTPUT_NAME "tessera_skia"
  PREFIX "lib"          # libtessera_skia.* on all platforms for a stable name
)
if(WIN32)
  set_target_properties(tessera_skia PROPERTIES PREFIX "")
endif()

# tessera_skia.cpp uses the CoreText SkFontMgr; that path pulls Obj-C headers,
# so compile the TU as Objective-C++ on Apple.
if(APPLE)
  set_source_files_properties(tessera_skia.cpp PROPERTIES
    COMPILE_FLAGS "-x objective-c++ -fobjc-arc")
endif()

# --- Skia / Dawn linkage (real build) ---------------------------------------
if(TESSERA_SKIA_STUB_ONLY)
  message(WARNING
    "tessera_skia: STUB_ONLY build — Skia/Dawn NOT linked. Every call returns "
    "TS_NOT_IMPLEMENTED. This is the scaffold escape hatch; the WP M3-06g "
    "default is a real Skia/Dawn link.")
else()
  if(NOT EXISTS "${TESSERA_NATIVE_STAGE}")
    message(FATAL_ERROR
      "tessera_skia: no staged Skia payload at ${TESSERA_NATIVE_STAGE}. "
      "Run native/build-skia.sh first (WP M3-06b).")
  endif()
  if(NOT EXISTS "${TESSERA_SKIA_SRC}/include/core/SkCanvas.h")
    message(FATAL_ERROR
      "tessera_skia: Skia source checkout not found at ${TESSERA_SKIA_SRC}. "
      "The shim build needs the source-tree header layout (include/core/...).")
  endif()
  if(NOT EXISTS "${TESSERA_DAWN_GEN}/include/dawn/webgpu_cpp.h")
    message(FATAL_ERROR
      "tessera_skia: Dawn generated headers not found at ${TESSERA_DAWN_GEN}. "
      "These are produced by the native/build-skia.* GN/Ninja build.")
  endif()

  # Header roots. Skia self-refs "include/...", so its root is the checkout
  # dir itself. Dawn static headers come from the checkout; Dawn *generated*
  # headers (webgpu_cpp.h, dawn_proc_table.h) come from the GN gen dir.
  target_include_directories(tessera_skia PRIVATE
    "${TESSERA_SKIA_SRC}"
    "${TESSERA_DAWN_SRC}/include"
    "${TESSERA_DAWN_GEN}/include"
  )

  # Skia + Dawn are built with these defines; the public headers branch on
  # them, so the shim TU must see the same set.
  target_compile_definitions(tessera_skia PRIVATE
    SK_GRAPHITE
    SK_DAWN
    SK_BUILD_FOR_MAC
    SK_GANESH=0
  )

  # Static libs staged by build-skia.* — fold Skia + Dawn + their bundled deps
  # into the one shim binary so .NET ships a single native file.
  #
  # Listed explicitly (not GLOB'd) and wrapped so the linker resolves the
  # circular refs between libskia and the Dawn static libs: on Apple we use
  # -force_load for the two libs whose symbols are pulled in indirectly
  # (the Dawn proc table + skia), and rely on multi-pass for the rest.
  set(TESSERA_SKIA_LIBS
    "${TESSERA_NATIVE_STAGE}/libskia.a"
    "${TESSERA_NATIVE_STAGE}/libdawn_native_static.a"
    "${TESSERA_NATIVE_STAGE}/libdawn_proc_static.a"
    "${TESSERA_NATIVE_STAGE}/libdawn_platform_static.a"
    "${TESSERA_NATIVE_STAGE}/libharfbuzz.a"
    "${TESSERA_NATIVE_STAGE}/libpng.a"
    "${TESSERA_NATIVE_STAGE}/libjpeg.a"
    "${TESSERA_NATIVE_STAGE}/libjpeg12.a"
    "${TESSERA_NATIVE_STAGE}/libjpeg16.a"
    "${TESSERA_NATIVE_STAGE}/libwebp.a"
    "${TESSERA_NATIVE_STAGE}/libwebp_sse41.a"
    "${TESSERA_NATIVE_STAGE}/libzlib.a"
    "${TESSERA_NATIVE_STAGE}/libexpat.a"
    "${TESSERA_NATIVE_STAGE}/libskcms.a"
    "${TESSERA_NATIVE_STAGE}/libspvtools.a"
    "${TESSERA_NATIVE_STAGE}/libspvtools_opt.a"
    "${TESSERA_NATIVE_STAGE}/libspvtools_val.a"
    "${TESSERA_NATIVE_STAGE}/libdng_sdk.a"
    "${TESSERA_NATIVE_STAGE}/libwuffs.a"
    "${TESSERA_NATIVE_STAGE}/libpiex.a"
    "${TESSERA_NATIVE_STAGE}/libcommon.a"
    "${TESSERA_NATIVE_STAGE}/libutils.a"
  )

  if(APPLE)
    # -force_load libskia + libdawn_native_static so their globally-registered
    # constructors / proc tables are not dead-stripped, and pass the whole set
    # twice to satisfy cross-archive circular references.
    target_link_libraries(tessera_skia PRIVATE
      -Wl,-force_load,"${TESSERA_NATIVE_STAGE}/libskia.a"
      -Wl,-force_load,"${TESSERA_NATIVE_STAGE}/libdawn_native_static.a"
      ${TESSERA_SKIA_LIBS}
      ${TESSERA_SKIA_LIBS}
    )
    target_link_libraries(tessera_skia PRIVATE
      "-framework Metal"
      "-framework Foundation"
      "-framework CoreFoundation"
      "-framework CoreGraphics"
      "-framework CoreText"
      "-framework CoreServices"
      "-framework QuartzCore"
      "-framework IOSurface"
      "-framework IOKit"
      "-framework Cocoa"
    )
  else()
    target_link_libraries(tessera_skia PRIVATE
      ${TESSERA_SKIA_LIBS}
      ${TESSERA_SKIA_LIBS}
    )
    if(WIN32)
      target_link_libraries(tessera_skia PRIVATE
        d3d12 dxgi dxguid d3dcompiler opengl32 user32 gdi32)
    elseif(UNIX)
      find_package(Threads REQUIRED)
      target_link_libraries(tessera_skia PRIVATE
        Threads::Threads dl vulkan GL X11)
    endif()
  endif()
endif()

# --- install: drop the shim back into the RID staging dir -------------------
install(TARGETS tessera_skia
  RUNTIME DESTINATION "${TESSERA_NATIVE_STAGE}"   # .dll  (Windows)
  LIBRARY DESTINATION "${TESSERA_NATIVE_STAGE}"   # .so / .dylib
)

# --- smoke test -------------------------------------------------------------
# A tiny C harness that exercises the full path: context -> surface -> clear +
# fill_rect -> flush_and_submit -> read_pixels, asserting pixel colors. Built
# only for the real (non-stub) shim.
if(NOT TESSERA_SKIA_STUB_ONLY)
  add_executable(tessera_skia_smoke smoke_test.c)
  target_include_directories(tessera_skia_smoke PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
  target_link_libraries(tessera_skia_smoke PRIVATE tessera_skia)
  add_custom_target(run_smoke
    COMMAND tessera_skia_smoke
    DEPENDS tessera_skia_smoke
    COMMENT "Running tessera_skia smoke test")
endif()
