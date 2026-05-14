# native/shim/CMakeLists.txt — builds libtessera_skia, the single native file
# .NET loads. Statically links the custom C ABI shim against libskia + Dawn.
#
# SCAFFOLD ONLY (WP M3-06a / Phase 0).
# ------------------------------------
# This CMake project is structured for the final build but is gated: it expects
# the Skia + Dawn artifacts that native/build-skia.* stages into
# runtimes/<rid>/native/. Until WP M3-06b has produced those, configuring with
# -DTESSERA_SKIA_STUB_ONLY=ON builds the shim with NO Skia link (stub bodies
# only) so src/Tessera.Skia (WP M3-06h) can validate its P/Invoke signatures
# against a real, loadable shared library. WP M3-06g flips the default to a
# real Skia/Dawn link and implements tessera_skia.cpp for real.

cmake_minimum_required(VERSION 3.20)
project(tessera_skia LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# --- options ----------------------------------------------------------------
# When ON, build the shim with stub bodies only — no Skia/Dawn link. This is the
# Phase 0 default so the project configures + builds on a machine that has not
# run native/build-skia.* yet. WP M3-06g sets this OFF.
option(TESSERA_SKIA_STUB_ONLY "Build the shim without linking Skia/Dawn (scaffold mode)" ON)

# --- resolve the .NET RID + the staged Skia/Dawn payload --------------------
# native/build-skia.* stages artifacts into <repo>/runtimes/<rid>/native/.
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
  # On Windows .NET resolves "tessera_skia" -> tessera_skia.dll; drop the prefix.
  set_target_properties(tessera_skia PROPERTIES PREFIX "")
endif()

# --- Skia / Dawn linkage (real build) ---------------------------------------
if(TESSERA_SKIA_STUB_ONLY)
  message(WARNING
    "tessera_skia: STUB_ONLY build — Skia/Dawn NOT linked. "
    "The resulting library loads and exports the C ABI but every call returns "
    "TS_NOT_IMPLEMENTED. WP M3-06g must build with -DTESSERA_SKIA_STUB_ONLY=OFF "
    "against a real libskia produced by native/build-skia.*.")
else()
  if(NOT EXISTS "${TESSERA_NATIVE_STAGE}")
    message(FATAL_ERROR
      "tessera_skia: -DTESSERA_SKIA_STUB_ONLY=OFF but no staged Skia payload at "
      "${TESSERA_NATIVE_STAGE}. Run native/build-skia.sh / build-skia.ps1 first "
      "(WP M3-06b).")
  endif()

  # Headers staged by build-skia.* (include/skia, include/dawn).
  target_include_directories(tessera_skia PRIVATE
    "${TESSERA_NATIVE_STAGE}/include/skia"
    "${TESSERA_NATIVE_STAGE}/include/dawn"
  )

  # Static libs staged by build-skia.* — Skia + Dawn fold into the one shim
  # binary so .NET loads a single native file with no transitive native deps.
  file(GLOB TESSERA_SKIA_STATIC_LIBS
    "${TESSERA_NATIVE_STAGE}/*.a"
    "${TESSERA_NATIVE_STAGE}/*.lib"
  )
  if(NOT TESSERA_SKIA_STATIC_LIBS)
    message(FATAL_ERROR
      "tessera_skia: no static libs found in ${TESSERA_NATIVE_STAGE} — "
      "did native/build-skia.* finish and stage artifacts?")
  endif()
  target_link_libraries(tessera_skia PRIVATE ${TESSERA_SKIA_STATIC_LIBS})

  # Platform system frameworks/libs Skia + Dawn need at link time.
  if(APPLE)
    target_link_libraries(tessera_skia PRIVATE
      "-framework Metal"
      "-framework Foundation"
      "-framework CoreGraphics"
      "-framework CoreText"
      "-framework QuartzCore"
      "-framework IOSurface"
    )
  elseif(WIN32)
    target_link_libraries(tessera_skia PRIVATE
      d3d12 dxgi dxguid d3dcompiler opengl32 user32 gdi32
    )
  elseif(UNIX)
    find_package(Threads REQUIRED)
    target_link_libraries(tessera_skia PRIVATE
      Threads::Threads dl
      vulkan GL X11
    )
  endif()
endif()

# --- install: drop the shim back into the RID staging dir -------------------
# WP M3-06h's csproj copies runtimes/<rid>/native/ into the .NET output.
install(TARGETS tessera_skia
  RUNTIME DESTINATION "${TESSERA_NATIVE_STAGE}"   # .dll  (Windows)
  LIBRARY DESTINATION "${TESSERA_NATIVE_STAGE}"   # .so / .dylib
)
