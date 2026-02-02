#pragma once

typedef struct APITable APITable;
typedef struct Method Method;
typedef struct Definition Definition;
typedef struct RuntimeState RuntimeState;
typedef struct Instance Instance;
typedef struct ReferenceLocal ReferenceLocal;

typedef Instance *(*InitFunc)(void);
typedef void (*FreeFunc)(Instance *thing);
typedef void (*ShowRefsFunc)(Instance *instance);
typedef void (*ShowStaticRefsFunc)(void);
typedef void (*GetDefinitionsFunc)(APITable *table);
typedef RuntimeState *(*RuntimeInitFunc)(void);
typedef bool (*RuntimeLoadPackageFunc)(const char *name, RuntimeState *state);
typedef Instance *(*RuntimeNewFunc)(RuntimeState *state, const char *namespace_, const char *name);
typedef void (*RuntimeStateInFunc)(RuntimeState *state);
typedef ReferenceLocal (*RuntimeLocalFunc)(RuntimeState *state, Instance **instance);
typedef void (*RuntimeAllocFunc)(RuntimeState *state, size_t size);
typedef void (*RuntimeShowInstanceFunc)(Instance *instance);
typedef void *(*RuntimeNullCoalesceFunc)(void *a, void *b);
typedef void *(*RuntimeUnwrapFunc)(void *a, int line);

typedef struct APITable
{
    Definition *defs;
    int count;
    RuntimeState *state;
    RuntimeInitFunc runtime_init;
    RuntimeLoadPackageFunc runtime_load_package;
    RuntimeNewFunc runtime_new;
    RuntimeStateInFunc runtime_free;
    RuntimeLocalFunc runtime_new_reference_local;
    RuntimeStateInFunc runtime_gc;
    RuntimeStateInFunc runtime_gc_force;
    RuntimeAllocFunc runtime_add_alloc;
    RuntimeAllocFunc runtime_sub_alloc;
    RuntimeShowInstanceFunc runtime_show_instance;
    RuntimeNullCoalesceFunc runtime_null_coalesce;
    RuntimeUnwrapFunc runtime_unwrap;
} APITable;

typedef struct Method
{
    char *name;
    void *entry;
} Method;

typedef struct Definition
{
    char *namespace_;
    char *name;
    Method *methods;
    int method_count;
    int instance_size;
    Instance **static_data;
    InitFunc new;
    FreeFunc free;
    ShowRefsFunc show_refs;
    ShowStaticRefsFunc show_static_refs;
} Definition;

typedef struct RuntimeState
{
    Definition **definitions;
    ReferenceLocal *locals;
    Instance **instances;
    DllHandle **dlls;
    size_t allocated_bytes;
    size_t gc_threshold;
} RuntimeState;

typedef struct Instance {
    Definition *definition;
    bool seen;
    Instance *data;
} Instance;

typedef struct ReferenceLocal 
{
    Instance **instance;
    ReferenceLocal *prev;
} ReferenceLocal;
