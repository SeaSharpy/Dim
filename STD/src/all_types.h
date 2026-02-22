#pragma once
#include "runtime.h"

typedef struct STD_String {
    Definition *definition;
    bool seen;
    const char *data;
} STD_String;

typedef struct STD_Any {
    Definition *definition;
    bool seen;
    Instance* f_0;
} STD_Any;

typedef struct STD_List {
    Definition *definition;
    bool seen;
    STD_Any **data;
} STD_List;
