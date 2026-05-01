cmake_minimum_required(VERSION 3.20)
project(emberkeep_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Day 1: stub only. Day 2: uncomment llama.cpp.
# add_subdirectory(llama.cpp)

add_library(emberkeep_native SHARED
    emberkeep_native/src/emberkeep_native.cpp
)
target_include_directories(emberkeep_native PUBLIC emberkeep_native/include)
target_compile_definitions(emberkeep_native PRIVATE EK_BUILDING_DLL)
# target_link_libraries(emberkeep_native PRIVATE llama)   # Day 2

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
