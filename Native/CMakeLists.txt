cmake_minimum_required(VERSION 3.20)
project(emberkeep_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# llama.cpp build options - lean build for Day 2 (CPU only, no examples/tests)
set(LLAMA_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_TESTS    OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_SERVER   OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_TOOLS    OFF CACHE BOOL "" FORCE)
set(LLAMA_CURL           OFF CACHE BOOL "" FORCE)
set(GGML_CCACHE          OFF CACHE BOOL "" FORCE)
set(GGML_BUILD_EXAMPLES  OFF CACHE BOOL "" FORCE)
set(GGML_BUILD_TESTS     OFF CACHE BOOL "" FORCE)
# CPU backend with AVX2 (covers ~all 2014+ Intel/AMD desktops)
set(GGML_NATIVE          ON  CACHE BOOL "" FORCE)
set(BUILD_SHARED_LIBS    OFF CACHE BOOL "" FORCE)

add_subdirectory(llama.cpp)

add_library(emberkeep_native SHARED
    emberkeep_native/src/emberkeep_native.cpp
)
target_include_directories(emberkeep_native PUBLIC emberkeep_native/include)
target_compile_definitions(emberkeep_native PRIVATE EK_BUILDING_DLL)
target_link_libraries(emberkeep_native PRIVATE llama)

if(WIN32)
    set_target_properties(emberkeep_native PROPERTIES
        PREFIX ""        # no "lib" prefix on Windows
        SUFFIX ".dll"
    )
elseif(APPLE)
    set_target_properties(emberkeep_native PROPERTIES
        BUNDLE TRUE
        BUNDLE_EXTENSION "bundle"
        PREFIX ""
    )
endif()
