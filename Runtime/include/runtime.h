#pragma once
// #define DEBUG
#if defined(_WIN32) || defined(__CYGWIN__)
#define EXPORT __declspec(dllexport)
#else
#if defined(__GNUC__) || defined(__clang__)
#define EXPORT __attribute__((visibility("default")))
#else
#define EXPORT
#endif
#endif

#include <inttypes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>
#include "platform_dll.h"
#include "platform_time.h"
#include "stb_ds.h"
#include "types.h"

#ifdef DEBUG
#define debugprintf(...) printf(__VA_ARGS__)
#else
#define debugprintf(...) ((void)0)
#endif

#define runtime_reference_local(state, instance, name)                  \
    ReferenceLocal name = runtime_new_reference_local(state, instance); \
    state->locals = &name;

#define value_local(type, id) \
    type l_##id = 0;          \
    (void)l_##id;

#define class_local(type, id)                                       \
    type l_##id = NULL;                                             \
    runtime_reference_local(state, (Instance **)&l_##id, l_r_##id); \
    (void)l_##id;

#define static_method(type_cast, class, index) \
    ((type_cast)(get_##class()->methods[(index)].entry))

#define static_method_call(type_cast, class, index, ...) \
    (static_method(type_cast, class, index)(__VA_ARGS__))

#define set_local(id, value) l_##id = value

#define use(value) (void)value

#define class_arg(id) runtime_reference_local(state, (Instance **)&p_##id, p_r_##id)

#define static_data(class) \
    ((static_##class *)get_##class()->static_data)

#define gc runtime_gc(state)
#define gc_force runtime_gc_force(state)

#define block_enter(id) \
    gc;                 \
    ReferenceLocal *l_prev_##id = state->locals;

#define block_exit(id)           \
    state->locals = l_prev_##id; \
    gc;

#define class_ret(type)                            \
    type l_retval = NULL;                          \
    ReferenceLocal *l_init_preret = state->locals; \
    runtime_reference_local(state, (Instance **)&l_retval, l_r_retval);

#define value_ret(type) type l_retval = 0;

#define method_start ReferenceLocal *l_init = state->locals

#define do_ret_value(x) \
    do                  \
    {                   \
        l_retval = (x); \
        goto _ret;      \
    } while (0)

#define do_ret_void goto _ret;

#define method_end          \
    goto _ret;              \
    _ret:                   \
    state->locals = l_init; \
    gc;

#define method_end_class_ret state->locals = l_init_preret

#define ret_value return l_retval

#define ret_void return

#ifdef FUNCTION_SIG
EXPORT RuntimeState *runtime_init();
EXPORT bool runtime_load_package(const char *name, RuntimeState *state);
EXPORT Instance *runtime_new(RuntimeState *state, const char *namespace_, const char *name);
EXPORT void runtime_free(RuntimeState *state);
EXPORT ReferenceLocal runtime_new_reference_local(RuntimeState *state, Instance **instance);
EXPORT void runtime_gc(RuntimeState *state);
EXPORT void runtime_gc_force(RuntimeState *state);
EXPORT void runtime_add_alloc(RuntimeState *state, size_t size);
EXPORT void runtime_sub_alloc(RuntimeState *state, size_t size);
EXPORT void runtime_show_instance(RuntimeState *state, Instance *instance);
EXPORT void *runtime_null_coalesce(void *a, void *b);
EXPORT void *runtime_unwrap(void *a, int line);
#else
#ifdef FUNCTION_VAR
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
#else
#ifdef FUNCTION_VAR_EXT
extern RuntimeInitFunc runtime_init;
extern RuntimeLoadPackageFunc runtime_load_package;
extern RuntimeNewFunc runtime_new;
extern RuntimeStateInFunc runtime_free;
extern RuntimeLocalFunc runtime_new_reference_local;
extern RuntimeStateInFunc runtime_gc;
extern RuntimeStateInFunc runtime_gc_force;
extern RuntimeAllocFunc runtime_add_alloc;
extern RuntimeAllocFunc runtime_sub_alloc;
extern RuntimeShowInstanceFunc runtime_show_instance;
extern RuntimeNullCoalesceFunc runtime_null_coalesce;
extern RuntimeUnwrapFunc runtime_unwrap;
#endif
#endif
#endif
