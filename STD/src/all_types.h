#pragma once
#include "runtime.h"
// STD::String
typedef struct STD_String {
    Definition *definition;
    bool seen;
    const char *data;
} STD_String;
// STD::Any
typedef struct STD_Any {
    Definition *definition;
    bool seen;
    Instance* f_0; // STD::Any.value
} STD_Any;
// STD::List
typedef struct STD_List {
    Definition *definition;
    bool seen;
    STD_Any **data;
} STD_List;
// STD::STD
typedef struct STD_STD {
    Definition *definition;
    bool seen;
} STD_STD;
