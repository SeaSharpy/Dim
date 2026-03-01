#define STB_DS_IMPLEMENTATION
#define FUNCTION_VAR
#include "runtime.h"
#include "all_types.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>
#include <inttypes.h>
#include <stdarg.h>
#include <math.h>

static RuntimeState *state = NULL;

static inline Definition *find_definition(const char *namespace_, const char *name)
{
    if (!state)
        return NULL;
    for (int i = 0; i < arrlen(state->definitions); i++)
    {
        Definition *def = state->definitions[i];
        if (strcmp(def->namespace_, namespace_) == 0 && strcmp(def->name, name) == 0)
            return def;
    }
    return NULL;
}

static inline Definition *ensure_definition(Definition **cache, const char *namespace_, const char *name)
{
    if (*cache)
        return *cache;
    Definition *def = find_definition(namespace_, name);
    if (!def)
    {
        printf("Missing definition %s::%s\n", namespace_, name);
        abort();
    }
    *cache = def;
    return def;
}

Definition *def_STD_String = NULL;
Definition *get_STD_String(void)
{
    return ensure_definition(&def_STD_String, "STD", "String");
}

EXPORT void getDefinitions(APITable *table);

static STD_String *new_STD_String(void)
{
    STD_String *instance = (STD_String *)malloc(sizeof(STD_String));
    instance->data = NULL;
    return instance;
}

static STD_Any *new_STD_Any(void)
{
    STD_Any *instance = (STD_Any *)malloc(sizeof(STD_Any));
    instance->f_0 = NULL;
    return instance;
}

static STD_List *new_STD_List(void)
{
    STD_List *instance = (STD_List *)malloc(sizeof(STD_List));
    instance->data = NULL;
    return instance;
}

static void free_STD_Any(STD_Any *instance)
{
    if (!instance)
        return;
    free(instance);
}

static void free_STD_List(STD_List *instance)
{
    if (!instance)
        return;
    if (instance->data)
    {
        size_t cap = (size_t)arrcap(instance->data);
        if (cap > 0)
            runtime_sub_alloc(state, cap * sizeof(STD_Any *));
        arrfree(instance->data);
        instance->data = NULL;
    }
    free(instance);
}

static STD_String *STD_String_New(const char *data)
{
    STD_String *instance = (STD_String *)runtime_new(state, "STD", "String");
    if (!data)
    {
        instance->data = NULL;
        return instance;
    }

    size_t len = strlen(data) + 1;
    char *copy = (char *)malloc(len);
    memcpy(copy, data, len);
    instance->data = copy;
    runtime_add_alloc(state, len);
    return instance;
}

static STD_String *STD_String_FromString(STD_String *p_0)
{
    if (!p_0 || !p_0->data)
        return STD_String_New("");
    return STD_String_New(p_0->data);
}

static STD_String *STD_String_Clone(STD_String *p_0)
{
    return STD_String_FromString(p_0);
}

static STD_String *STD_String_Concat(STD_String *p_0, STD_String *p_1)
{
    const char *s0 = (p_0 && p_0->data) ? p_0->data : "";
    const char *s1 = (p_1 && p_1->data) ? p_1->data : "";

    size_t len0 = strlen(s0);
    size_t len1 = strlen(s1);

    size_t len = len0 + len1 + 1;
    char *data = (char *)malloc(len);

    memcpy(data, s0, len0);
    memcpy(data + len0, s1, len1);
    data[len0 + len1] = '\0';

    STD_String *instance = STD_String_New(data);
    free(data);
    return instance;
}

static STD_String *STD_String_FromFormat(const char *fmt, ...)
{
    va_list args;
    va_start(args, fmt);

    va_list args2;
    va_copy(args2, args);

    int needed = vsnprintf(NULL, 0, fmt, args);
    va_end(args);

    if (needed < 0)
    {
        va_end(args2);
        return STD_String_New("");
    }

    size_t len = (size_t)needed + 1;
    char *buf = (char *)malloc(len);
    vsnprintf(buf, len, fmt, args2);
    va_end(args2);

    STD_String *instance = STD_String_New(buf);
    free(buf);
    return instance;
}

static STD_String *STD_String_FromBool(bool p_0)
{
    return STD_String_New(p_0 ? "true" : "false");
}

static STD_String *STD_String_FromInt(int32_t p_0)
{
    return STD_String_FromFormat("%" PRId32, p_0);
}

static STD_String *STD_String_FromUInt(uint32_t p_0)
{
    return STD_String_FromFormat("%" PRIu32, p_0);
}

static STD_String *STD_String_FromLong(int64_t p_0)
{
    return STD_String_FromFormat("%" PRId64, p_0);
}

static STD_String *STD_String_FromULong(uint64_t p_0)
{
    return STD_String_FromFormat("%" PRIu64, p_0);
}

static STD_String *STD_String_FromFloat(float p_0)
{
    return STD_String_FromFormat("%g", (double)p_0);
}

static STD_String *STD_String_FromDouble(double p_0)
{
    return STD_String_FromFormat("%g", p_0);
}

static STD_String *STD_String_FromByte(uint8_t p_0)
{
    return STD_String_FromFormat("%" PRIu8, p_0);
}

static STD_String *STD_String_FromSByte(int8_t p_0)
{
    return STD_String_FromFormat("%" PRId8, p_0);
}

static STD_String *STD_String_FromChar(char p_0)
{
    char buf[2];
    buf[0] = p_0;
    buf[1] = '\0';
    return STD_String_New(buf);
}

static STD_String *STD_String_FromShort(int16_t p_0)
{
    return STD_String_FromFormat("%" PRId16, p_0);
}

static STD_String *STD_String_FromUShort(uint16_t p_0)
{
    return STD_String_FromFormat("%" PRIu16, p_0);
}

static int32_t STD_String_Length(STD_String *p_0)
{
    if (!p_0 || !p_0->data)
        return 0;
    return (int32_t)strlen(p_0->data);
}

static bool STD_String_IsEmpty(STD_String *p_0)
{
    if (!p_0 || !p_0->data)
        return true;
    return p_0->data[0] == '\0';
}

static bool STD_String_Equals(STD_String *p_0, STD_String *p_1)
{
    const char *s0 = (p_0 && p_0->data) ? p_0->data : "";
    const char *s1 = (p_1 && p_1->data) ? p_1->data : "";
    return strcmp(s0, s1) == 0;
}

static int32_t STD_String_Compare(STD_String *p_0, STD_String *p_1)
{
    const char *s0 = (p_0 && p_0->data) ? p_0->data : "";
    const char *s1 = (p_1 && p_1->data) ? p_1->data : "";
    int r = strcmp(s0, s1);
    if (r < 0)
        return -1;
    if (r > 0)
        return 1;
    return 0;
}

static void free_STD_String(STD_String *instance)
{
    if (!instance)
        return;
    if (instance->data)
        runtime_sub_alloc(state, strlen(instance->data) + 1);
    free((void *)instance->data);
    free(instance);
}

static void show_refs_STD_Any(Instance *instance)
{
    STD_Any *any = (STD_Any *)instance;
    if (!any)
        return;
    if (any->f_0)
        runtime_show_instance(state, any->f_0);
}

static STD_Any *STD_String_Box(STD_String *p_0)
{
    STD_Any *any = (STD_Any *)runtime_new(state, "STD", "Any");
    any->f_0 = (Instance *)p_0;
    return any;
}

static STD_String *STD_String_Unbox(STD_Any *p_0)
{
    if (!p_0 || !p_0->f_0)
        return NULL;
    Instance *inst = p_0->f_0;
    Definition *def = inst->definition;
    if (!def)
        return NULL;
    if (def == get_STD_String())
        return (STD_String *)inst;
    return NULL;
}

static void show_refs_STD_List(Instance *instance)
{
    STD_List *list = (STD_List *)instance;
    if (!list || !list->data)
        return;
    int len = arrlen(list->data);
    for (int i = 0; i < len; i++)
    {
        STD_Any *any = list->data[i];
        if (any)
            runtime_show_instance(state, (Instance *)any);
    }
}

static STD_List *STD_List_New(void)
{
    return (STD_List *)runtime_new(state, "STD", "List");
}

static void STD_List_Add(STD_List *p_0, STD_Any *p_1)
{
    if (!p_0)
        return;
    size_t oldCap = (size_t)arrcap(p_0->data);
    arrput(p_0->data, p_1);
    size_t newCap = (size_t)arrcap(p_0->data);
    if (newCap > oldCap)
        runtime_add_alloc(state, (newCap - oldCap) * sizeof(STD_Any *));
}

static int32_t STD_List_Count(STD_List *p_0)
{
    if (!p_0 || !p_0->data)
        return 0;
    return (int32_t)arrlen(p_0->data);
}

static STD_Any *STD_List_Get(STD_List *p_0, int32_t index)
{
    if (!p_0 || !p_0->data)
        return NULL;
    int len = arrlen(p_0->data);
    if (index < 0 || index >= len)
        return NULL;
    return p_0->data[index];
}

static void STD_List_Set(STD_List *p_0, int32_t index, STD_Any *value)
{
    if (!p_0 || !p_0->data)
        return;
    int len = arrlen(p_0->data);
    if (index < 0 || index >= len)
        return;
    p_0->data[index] = value;
}

static STD_Any *STD_List_Pop(STD_List *p_0)
{
    if (!p_0 || !p_0->data)
        return NULL;
    if (arrlen(p_0->data) == 0)
        return NULL;
    return arrpop(p_0->data);
}

static void STD_List_RemoveAt(STD_List *p_0, int32_t index)
{
    if (!p_0 || !p_0->data)
        return;
    int len = arrlen(p_0->data);
    if (index < 0 || index >= len)
        return;
    arrdel(p_0->data, index);
}

static void STD_List_Clear(STD_List *p_0)
{
    if (!p_0 || !p_0->data)
        return;
    size_t cap = (size_t)arrcap(p_0->data);
    if (cap > 0)
        runtime_sub_alloc(state, cap * sizeof(STD_Any *));
    arrfree(p_0->data);
    p_0->data = NULL;
}

void STD_STD_Print(STD_String *p_0)
{
    if (!p_0 || !p_0->data)
        return;
    printf("%s\n", p_0->data);
}

double STD_STD_TimeMS(void)
{
    return time_ms();
}

static double STD_Math_Sqrt(double p_0) { return sqrt(p_0); }
static double STD_Math_Pow(double p_0, double p_1) { return pow(p_0, p_1); }
static double STD_Math_Sin(double p_0) { return sin(p_0); }
static double STD_Math_Cos(double p_0) { return cos(p_0); }
static double STD_Math_Tan(double p_0) { return tan(p_0); }
static double STD_Math_Asin(double p_0) { return asin(p_0); }
static double STD_Math_Acos(double p_0) { return acos(p_0); }
static double STD_Math_Atan(double p_0) { return atan(p_0); }
static double STD_Math_Atan2(double p_0, double p_1) { return atan2(p_0, p_1); }

static double STD_Math_Exp(double p_0) { return exp(p_0); }
static double STD_Math_Log(double p_0) { return log(p_0); }
static double STD_Math_Log10(double p_0) { return log10(p_0); }

static double STD_Math_Floor(double p_0) { return floor(p_0); }
static double STD_Math_Ceil(double p_0) { return ceil(p_0); }
static double STD_Math_Round(double p_0) { return round(p_0); }

static double STD_Math_Fmod(double p_0, double p_1) { return fmod(p_0, p_1); }

static double STD_Math_Abs(double p_0) { return fabs(p_0); }
static double STD_Math_Min(double p_0, double p_1) { return (p_0 < p_1) ? p_0 : p_1; }
static double STD_Math_Max(double p_0, double p_1) { return (p_0 > p_1) ? p_0 : p_1; }

static float STD_MathF_Sqrt(float p_0) { return sqrtf(p_0); }
static float STD_MathF_Pow(float p_0, float p_1) { return powf(p_0, p_1); }
static float STD_MathF_Sin(float p_0) { return sinf(p_0); }
static float STD_MathF_Cos(float p_0) { return cosf(p_0); }
static float STD_MathF_Tan(float p_0) { return tanf(p_0); }
static float STD_MathF_Asin(float p_0) { return asinf(p_0); }
static float STD_MathF_Acos(float p_0) { return acosf(p_0); }
static float STD_MathF_Atan(float p_0) { return atanf(p_0); }
static float STD_MathF_Atan2(float p_0, float p_1) { return atan2f(p_0, p_1); }

static float STD_MathF_Exp(float p_0) { return expf(p_0); }
static float STD_MathF_Log(float p_0) { return logf(p_0); }
static float STD_MathF_Log10(float p_0) { return log10f(p_0); }

static float STD_MathF_Floor(float p_0) { return floorf(p_0); }
static float STD_MathF_Ceil(float p_0) { return ceilf(p_0); }
static float STD_MathF_Round(float p_0) { return roundf(p_0); }

static float STD_MathF_Fmod(float p_0, float p_1) { return fmodf(p_0, p_1); }

static float STD_MathF_Abs(float p_0) { return fabsf(p_0); }
static float STD_MathF_Min(float p_0, float p_1) { return (p_0 < p_1) ? p_0 : p_1; }
static float STD_MathF_Max(float p_0, float p_1) { return (p_0 > p_1) ? p_0 : p_1; }

static int32_t STD_MathI_MinInt(int32_t a, int32_t b) { return (a < b) ? a : b; }
static int32_t STD_MathI_MaxInt(int32_t a, int32_t b) { return (a > b) ? a : b; }
static int32_t STD_MathI_ClampInt(int32_t v, int32_t lo, int32_t hi)
{
    if (hi < lo)
    {
        int32_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}
static int32_t STD_MathI_AbsInt(int32_t v) { return (v < 0) ? -v : v; }

static uint32_t STD_MathI_MinUInt(uint32_t a, uint32_t b) { return (a < b) ? a : b; }
static uint32_t STD_MathI_MaxUInt(uint32_t a, uint32_t b) { return (a > b) ? a : b; }
static uint32_t STD_MathI_ClampUInt(uint32_t v, uint32_t lo, uint32_t hi)
{
    if (hi < lo)
    {
        uint32_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}

static int64_t STD_MathI_MinLong(int64_t a, int64_t b) { return (a < b) ? a : b; }
static int64_t STD_MathI_MaxLong(int64_t a, int64_t b) { return (a > b) ? a : b; }
static int64_t STD_MathI_ClampLong(int64_t v, int64_t lo, int64_t hi)
{
    if (hi < lo)
    {
        int64_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}
static int64_t STD_MathI_AbsLong(int64_t v) { return (v < 0) ? -v : v; }

static uint64_t STD_MathI_MinULong(uint64_t a, uint64_t b) { return (a < b) ? a : b; }
static uint64_t STD_MathI_MaxULong(uint64_t a, uint64_t b) { return (a > b) ? a : b; }
static uint64_t STD_MathI_ClampULong(uint64_t v, uint64_t lo, uint64_t hi)
{
    if (hi < lo)
    {
        uint64_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}

static int16_t STD_MathI_MinShort(int16_t a, int16_t b) { return (a < b) ? a : b; }
static int16_t STD_MathI_MaxShort(int16_t a, int16_t b) { return (a > b) ? a : b; }
static int16_t STD_MathI_ClampShort(int16_t v, int16_t lo, int16_t hi)
{
    if (hi < lo)
    {
        int16_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}
static int16_t STD_MathI_AbsShort(int16_t v) { return (v < 0) ? (int16_t)-v : v; }

static uint16_t STD_MathI_MinUShort(uint16_t a, uint16_t b) { return (a < b) ? a : b; }
static uint16_t STD_MathI_MaxUShort(uint16_t a, uint16_t b) { return (a > b) ? a : b; }
static uint16_t STD_MathI_ClampUShort(uint16_t v, uint16_t lo, uint16_t hi)
{
    if (hi < lo)
    {
        uint16_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}

static int8_t STD_MathI_MinSByte(int8_t a, int8_t b) { return (a < b) ? a : b; }
static int8_t STD_MathI_MaxSByte(int8_t a, int8_t b) { return (a > b) ? a : b; }
static int8_t STD_MathI_ClampSByte(int8_t v, int8_t lo, int8_t hi)
{
    if (hi < lo)
    {
        int8_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}
static int8_t STD_MathI_AbsSByte(int8_t v) { return (v < 0) ? (int8_t)-v : v; }

static uint8_t STD_MathI_MinByte(uint8_t a, uint8_t b) { return (a < b) ? a : b; }
static uint8_t STD_MathI_MaxByte(uint8_t a, uint8_t b) { return (a > b) ? a : b; }
static uint8_t STD_MathI_ClampByte(uint8_t v, uint8_t lo, uint8_t hi)
{
    if (hi < lo)
    {
        uint8_t t = lo;
        lo = hi;
        hi = t;
    }
    if (v < lo)
        return lo;
    if (v > hi)
        return hi;
    return v;
}

static bool STD_MathC_BoolFromBool(bool v) { return v; }
static bool STD_MathC_BoolFromInt(int32_t v) { return v != 0; }
static bool STD_MathC_BoolFromUInt(uint32_t v) { return v != 0; }
static bool STD_MathC_BoolFromLong(int64_t v) { return v != 0; }
static bool STD_MathC_BoolFromULong(uint64_t v) { return v != 0; }
static bool STD_MathC_BoolFromFloat(float v) { return v != 0.0f; }
static bool STD_MathC_BoolFromDouble(double v) { return v != 0.0; }
static bool STD_MathC_BoolFromByte(uint8_t v) { return v != 0; }
static bool STD_MathC_BoolFromSByte(int8_t v) { return v != 0; }
static bool STD_MathC_BoolFromChar(char v) { return v != 0; }
static bool STD_MathC_BoolFromShort(int16_t v) { return v != 0; }
static bool STD_MathC_BoolFromUShort(uint16_t v) { return v != 0; }

static int32_t STD_MathC_IntFromBool(bool v) { return v ? 1 : 0; }
static int32_t STD_MathC_IntFromInt(int32_t v) { return v; }
static int32_t STD_MathC_IntFromUInt(uint32_t v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromLong(int64_t v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromULong(uint64_t v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromFloat(float v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromDouble(double v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromByte(uint8_t v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromSByte(int8_t v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromChar(char v) { return (int32_t)(unsigned char)v; }
static int32_t STD_MathC_IntFromShort(int16_t v) { return (int32_t)v; }
static int32_t STD_MathC_IntFromUShort(uint16_t v) { return (int32_t)v; }

static uint32_t STD_MathC_UIntFromBool(bool v) { return v ? 1u : 0u; }
static uint32_t STD_MathC_UIntFromInt(int32_t v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromUInt(uint32_t v) { return v; }
static uint32_t STD_MathC_UIntFromLong(int64_t v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromULong(uint64_t v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromFloat(float v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromDouble(double v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromByte(uint8_t v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromSByte(int8_t v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromChar(char v) { return (uint32_t)(unsigned char)v; }
static uint32_t STD_MathC_UIntFromShort(int16_t v) { return (uint32_t)v; }
static uint32_t STD_MathC_UIntFromUShort(uint16_t v) { return (uint32_t)v; }

static int64_t STD_MathC_LongFromBool(bool v) { return v ? 1 : 0; }
static int64_t STD_MathC_LongFromInt(int32_t v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromUInt(uint32_t v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromLong(int64_t v) { return v; }
static int64_t STD_MathC_LongFromULong(uint64_t v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromFloat(float v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromDouble(double v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromByte(uint8_t v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromSByte(int8_t v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromChar(char v) { return (int64_t)(unsigned char)v; }
static int64_t STD_MathC_LongFromShort(int16_t v) { return (int64_t)v; }
static int64_t STD_MathC_LongFromUShort(uint16_t v) { return (int64_t)v; }

static uint64_t STD_MathC_ULongFromBool(bool v) { return v ? 1ull : 0ull; }
static uint64_t STD_MathC_ULongFromInt(int32_t v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromUInt(uint32_t v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromLong(int64_t v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromULong(uint64_t v) { return v; }
static uint64_t STD_MathC_ULongFromFloat(float v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromDouble(double v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromByte(uint8_t v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromSByte(int8_t v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromChar(char v) { return (uint64_t)(unsigned char)v; }
static uint64_t STD_MathC_ULongFromShort(int16_t v) { return (uint64_t)v; }
static uint64_t STD_MathC_ULongFromUShort(uint16_t v) { return (uint64_t)v; }

static float STD_MathC_FloatFromBool(bool v) { return v ? 1.0f : 0.0f; }
static float STD_MathC_FloatFromInt(int32_t v) { return (float)v; }
static float STD_MathC_FloatFromUInt(uint32_t v) { return (float)v; }
static float STD_MathC_FloatFromLong(int64_t v) { return (float)v; }
static float STD_MathC_FloatFromULong(uint64_t v) { return (float)v; }
static float STD_MathC_FloatFromFloat(float v) { return v; }
static float STD_MathC_FloatFromDouble(double v) { return (float)v; }
static float STD_MathC_FloatFromByte(uint8_t v) { return (float)v; }
static float STD_MathC_FloatFromSByte(int8_t v) { return (float)v; }
static float STD_MathC_FloatFromChar(char v) { return (float)(unsigned char)v; }
static float STD_MathC_FloatFromShort(int16_t v) { return (float)v; }
static float STD_MathC_FloatFromUShort(uint16_t v) { return (float)v; }

static double STD_MathC_DoubleFromBool(bool v) { return v ? 1.0 : 0.0; }
static double STD_MathC_DoubleFromInt(int32_t v) { return (double)v; }
static double STD_MathC_DoubleFromUInt(uint32_t v) { return (double)v; }
static double STD_MathC_DoubleFromLong(int64_t v) { return (double)v; }
static double STD_MathC_DoubleFromULong(uint64_t v) { return (double)v; }
static double STD_MathC_DoubleFromFloat(float v) { return (double)v; }
static double STD_MathC_DoubleFromDouble(double v) { return v; }
static double STD_MathC_DoubleFromByte(uint8_t v) { return (double)v; }
static double STD_MathC_DoubleFromSByte(int8_t v) { return (double)v; }
static double STD_MathC_DoubleFromChar(char v) { return (double)(unsigned char)v; }
static double STD_MathC_DoubleFromShort(int16_t v) { return (double)v; }
static double STD_MathC_DoubleFromUShort(uint16_t v) { return (double)v; }

static uint8_t STD_MathC_ByteFromBool(bool v) { return v ? (uint8_t)1 : (uint8_t)0; }
static uint8_t STD_MathC_ByteFromInt(int32_t v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromUInt(uint32_t v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromLong(int64_t v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromULong(uint64_t v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromFloat(float v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromDouble(double v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromByte(uint8_t v) { return v; }
static uint8_t STD_MathC_ByteFromSByte(int8_t v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromChar(char v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromShort(int16_t v) { return (uint8_t)v; }
static uint8_t STD_MathC_ByteFromUShort(uint16_t v) { return (uint8_t)v; }

static int8_t STD_MathC_SByteFromBool(bool v) { return v ? (int8_t)1 : (int8_t)0; }
static int8_t STD_MathC_SByteFromInt(int32_t v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromUInt(uint32_t v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromLong(int64_t v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromULong(uint64_t v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromFloat(float v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromDouble(double v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromByte(uint8_t v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromSByte(int8_t v) { return v; }
static int8_t STD_MathC_SByteFromChar(char v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromShort(int16_t v) { return (int8_t)v; }
static int8_t STD_MathC_SByteFromUShort(uint16_t v) { return (int8_t)v; }

static char STD_MathC_CharFromBool(bool v) { return v ? (char)1 : (char)0; }
static char STD_MathC_CharFromInt(int32_t v) { return (char)v; }
static char STD_MathC_CharFromUInt(uint32_t v) { return (char)v; }
static char STD_MathC_CharFromLong(int64_t v) { return (char)v; }
static char STD_MathC_CharFromULong(uint64_t v) { return (char)v; }
static char STD_MathC_CharFromFloat(float v) { return (char)v; }
static char STD_MathC_CharFromDouble(double v) { return (char)v; }
static char STD_MathC_CharFromByte(uint8_t v) { return (char)v; }
static char STD_MathC_CharFromSByte(int8_t v) { return (char)v; }
static char STD_MathC_CharFromChar(char v) { return v; }
static char STD_MathC_CharFromShort(int16_t v) { return (char)v; }
static char STD_MathC_CharFromUShort(uint16_t v) { return (char)v; }

static int16_t STD_MathC_ShortFromBool(bool v) { return v ? (int16_t)1 : (int16_t)0; }
static int16_t STD_MathC_ShortFromInt(int32_t v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromUInt(uint32_t v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromLong(int64_t v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromULong(uint64_t v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromFloat(float v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromDouble(double v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromByte(uint8_t v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromSByte(int8_t v) { return (int16_t)v; }
static int16_t STD_MathC_ShortFromChar(char v) { return (int16_t)(unsigned char)v; }
static int16_t STD_MathC_ShortFromShort(int16_t v) { return v; }
static int16_t STD_MathC_ShortFromUShort(uint16_t v) { return (int16_t)v; }

static uint16_t STD_MathC_UShortFromBool(bool v) { return v ? (uint16_t)1 : (uint16_t)0; }
static uint16_t STD_MathC_UShortFromInt(int32_t v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromUInt(uint32_t v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromLong(int64_t v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromULong(uint64_t v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromFloat(float v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromDouble(double v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromByte(uint8_t v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromSByte(int8_t v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromChar(char v) { return (uint16_t)(unsigned char)v; }
static uint16_t STD_MathC_UShortFromShort(int16_t v) { return (uint16_t)v; }
static uint16_t STD_MathC_UShortFromUShort(uint16_t v) { return v; }

static Method STD_String_methods[] = {
    {"New", (void *)STD_String_New},
    {"FromString", (void *)STD_String_FromString},
    {"Clone", (void *)STD_String_Clone},
    {"Concat", (void *)STD_String_Concat},

    {"FromBool", (void *)STD_String_FromBool},
    {"FromInt", (void *)STD_String_FromInt},
    {"FromUInt", (void *)STD_String_FromUInt},
    {"FromLong", (void *)STD_String_FromLong},
    {"FromULong", (void *)STD_String_FromULong},
    {"FromFloat", (void *)STD_String_FromFloat},
    {"FromDouble", (void *)STD_String_FromDouble},
    {"FromByte", (void *)STD_String_FromByte},
    {"FromSByte", (void *)STD_String_FromSByte},
    {"FromChar", (void *)STD_String_FromChar},
    {"FromShort", (void *)STD_String_FromShort},
    {"FromUShort", (void *)STD_String_FromUShort},

    {"Length", (void *)STD_String_Length},
    {"IsEmpty", (void *)STD_String_IsEmpty},
    {"Equals", (void *)STD_String_Equals},
    {"Compare", (void *)STD_String_Compare},
    {"Box", (void *)STD_String_Box},
    {"Unbox", (void *)STD_String_Unbox},
};

static Method STD_List_methods[] = {
    {"New", (void *)STD_List_New},
    {"Add", (void *)STD_List_Add},
    {"Count", (void *)STD_List_Count},
    {"Get", (void *)STD_List_Get},
    {"Set", (void *)STD_List_Set},
    {"Pop", (void *)STD_List_Pop},
    {"RemoveAt", (void *)STD_List_RemoveAt},
    {"Clear", (void *)STD_List_Clear},
};

static Method STD_STD_methods[] = {
    {"Print", (void *)STD_STD_Print},
    {"TimeMS", (void *)STD_STD_TimeMS},
};

static Method STD_Math_methods[] = {
    {"Sqrt", (void *)STD_Math_Sqrt},
    {"Pow", (void *)STD_Math_Pow},

    {"Sin", (void *)STD_Math_Sin},
    {"Cos", (void *)STD_Math_Cos},
    {"Tan", (void *)STD_Math_Tan},
    {"Asin", (void *)STD_Math_Asin},
    {"Acos", (void *)STD_Math_Acos},
    {"Atan", (void *)STD_Math_Atan},
    {"Atan2", (void *)STD_Math_Atan2},

    {"Exp", (void *)STD_Math_Exp},
    {"Log", (void *)STD_Math_Log},
    {"Log10", (void *)STD_Math_Log10},

    {"Floor", (void *)STD_Math_Floor},
    {"Ceil", (void *)STD_Math_Ceil},
    {"Round", (void *)STD_Math_Round},

    {"Fmod", (void *)STD_Math_Fmod},

    {"Abs", (void *)STD_Math_Abs},
    {"Min", (void *)STD_Math_Min},
    {"Max", (void *)STD_Math_Max},
};

static Method STD_MathF_methods[] = {
    {"Sqrt", (void *)STD_MathF_Sqrt},
    {"Pow", (void *)STD_MathF_Pow},

    {"Sin", (void *)STD_MathF_Sin},
    {"Cos", (void *)STD_MathF_Cos},
    {"Tan", (void *)STD_MathF_Tan},
    {"Asin", (void *)STD_MathF_Asin},
    {"Acos", (void *)STD_MathF_Acos},
    {"Atan", (void *)STD_MathF_Atan},
    {"Atan2", (void *)STD_MathF_Atan2},

    {"Exp", (void *)STD_MathF_Exp},
    {"Log", (void *)STD_MathF_Log},
    {"Log10", (void *)STD_MathF_Log10},

    {"Floor", (void *)STD_MathF_Floor},
    {"Ceil", (void *)STD_MathF_Ceil},
    {"Round", (void *)STD_MathF_Round},

    {"Fmod", (void *)STD_MathF_Fmod},

    {"Abs", (void *)STD_MathF_Abs},
    {"Min", (void *)STD_MathF_Min},
    {"Max", (void *)STD_MathF_Max},
};

static Method STD_MathI_methods[] = {
    {"MinInt", (void *)STD_MathI_MinInt},
    {"MaxInt", (void *)STD_MathI_MaxInt},
    {"ClampInt", (void *)STD_MathI_ClampInt},
    {"AbsInt", (void *)STD_MathI_AbsInt},

    {"MinUInt", (void *)STD_MathI_MinUInt},
    {"MaxUInt", (void *)STD_MathI_MaxUInt},
    {"ClampUInt", (void *)STD_MathI_ClampUInt},

    {"MinLong", (void *)STD_MathI_MinLong},
    {"MaxLong", (void *)STD_MathI_MaxLong},
    {"ClampLong", (void *)STD_MathI_ClampLong},
    {"AbsLong", (void *)STD_MathI_AbsLong},

    {"MinULong", (void *)STD_MathI_MinULong},
    {"MaxULong", (void *)STD_MathI_MaxULong},
    {"ClampULong", (void *)STD_MathI_ClampULong},

    {"MinShort", (void *)STD_MathI_MinShort},
    {"MaxShort", (void *)STD_MathI_MaxShort},
    {"ClampShort", (void *)STD_MathI_ClampShort},
    {"AbsShort", (void *)STD_MathI_AbsShort},

    {"MinUShort", (void *)STD_MathI_MinUShort},
    {"MaxUShort", (void *)STD_MathI_MaxUShort},
    {"ClampUShort", (void *)STD_MathI_ClampUShort},

    {"MinSByte", (void *)STD_MathI_MinSByte},
    {"MaxSByte", (void *)STD_MathI_MaxSByte},
    {"ClampSByte", (void *)STD_MathI_ClampSByte},
    {"AbsSByte", (void *)STD_MathI_AbsSByte},

    {"MinByte", (void *)STD_MathI_MinByte},
    {"MaxByte", (void *)STD_MathI_MaxByte},
    {"ClampByte", (void *)STD_MathI_ClampByte},
};

static Method STD_MathC_methods[] = {
    {"BoolFromBool", (void *)STD_MathC_BoolFromBool},
    {"BoolFromInt", (void *)STD_MathC_BoolFromInt},
    {"BoolFromUInt", (void *)STD_MathC_BoolFromUInt},
    {"BoolFromLong", (void *)STD_MathC_BoolFromLong},
    {"BoolFromULong", (void *)STD_MathC_BoolFromULong},
    {"BoolFromFloat", (void *)STD_MathC_BoolFromFloat},
    {"BoolFromDouble", (void *)STD_MathC_BoolFromDouble},
    {"BoolFromByte", (void *)STD_MathC_BoolFromByte},
    {"BoolFromSByte", (void *)STD_MathC_BoolFromSByte},
    {"BoolFromChar", (void *)STD_MathC_BoolFromChar},
    {"BoolFromShort", (void *)STD_MathC_BoolFromShort},
    {"BoolFromUShort", (void *)STD_MathC_BoolFromUShort},

    {"IntFromBool", (void *)STD_MathC_IntFromBool},
    {"IntFromInt", (void *)STD_MathC_IntFromInt},
    {"IntFromUInt", (void *)STD_MathC_IntFromUInt},
    {"IntFromLong", (void *)STD_MathC_IntFromLong},
    {"IntFromULong", (void *)STD_MathC_IntFromULong},
    {"IntFromFloat", (void *)STD_MathC_IntFromFloat},
    {"IntFromDouble", (void *)STD_MathC_IntFromDouble},
    {"IntFromByte", (void *)STD_MathC_IntFromByte},
    {"IntFromSByte", (void *)STD_MathC_IntFromSByte},
    {"IntFromChar", (void *)STD_MathC_IntFromChar},
    {"IntFromShort", (void *)STD_MathC_IntFromShort},
    {"IntFromUShort", (void *)STD_MathC_IntFromUShort},

    {"UIntFromBool", (void *)STD_MathC_UIntFromBool},
    {"UIntFromInt", (void *)STD_MathC_UIntFromInt},
    {"UIntFromUInt", (void *)STD_MathC_UIntFromUInt},
    {"UIntFromLong", (void *)STD_MathC_UIntFromLong},
    {"UIntFromULong", (void *)STD_MathC_UIntFromULong},
    {"UIntFromFloat", (void *)STD_MathC_UIntFromFloat},
    {"UIntFromDouble", (void *)STD_MathC_UIntFromDouble},
    {"UIntFromByte", (void *)STD_MathC_UIntFromByte},
    {"UIntFromSByte", (void *)STD_MathC_UIntFromSByte},
    {"UIntFromChar", (void *)STD_MathC_UIntFromChar},
    {"UIntFromShort", (void *)STD_MathC_UIntFromShort},
    {"UIntFromUShort", (void *)STD_MathC_UIntFromUShort},

    {"LongFromBool", (void *)STD_MathC_LongFromBool},
    {"LongFromInt", (void *)STD_MathC_LongFromInt},
    {"LongFromUInt", (void *)STD_MathC_LongFromUInt},
    {"LongFromLong", (void *)STD_MathC_LongFromLong},
    {"LongFromULong", (void *)STD_MathC_LongFromULong},
    {"LongFromFloat", (void *)STD_MathC_LongFromFloat},
    {"LongFromDouble", (void *)STD_MathC_LongFromDouble},
    {"LongFromByte", (void *)STD_MathC_LongFromByte},
    {"LongFromSByte", (void *)STD_MathC_LongFromSByte},
    {"LongFromChar", (void *)STD_MathC_LongFromChar},
    {"LongFromShort", (void *)STD_MathC_LongFromShort},
    {"LongFromUShort", (void *)STD_MathC_LongFromUShort},

    {"ULongFromBool", (void *)STD_MathC_ULongFromBool},
    {"ULongFromInt", (void *)STD_MathC_ULongFromInt},
    {"ULongFromUInt", (void *)STD_MathC_ULongFromUInt},
    {"ULongFromLong", (void *)STD_MathC_ULongFromLong},
    {"ULongFromULong", (void *)STD_MathC_ULongFromULong},
    {"ULongFromFloat", (void *)STD_MathC_ULongFromFloat},
    {"ULongFromDouble", (void *)STD_MathC_ULongFromDouble},
    {"ULongFromByte", (void *)STD_MathC_ULongFromByte},
    {"ULongFromSByte", (void *)STD_MathC_ULongFromSByte},
    {"ULongFromChar", (void *)STD_MathC_ULongFromChar},
    {"ULongFromShort", (void *)STD_MathC_ULongFromShort},
    {"ULongFromUShort", (void *)STD_MathC_ULongFromUShort},

    {"FloatFromBool", (void *)STD_MathC_FloatFromBool},
    {"FloatFromInt", (void *)STD_MathC_FloatFromInt},
    {"FloatFromUInt", (void *)STD_MathC_FloatFromUInt},
    {"FloatFromLong", (void *)STD_MathC_FloatFromLong},
    {"FloatFromULong", (void *)STD_MathC_FloatFromULong},
    {"FloatFromFloat", (void *)STD_MathC_FloatFromFloat},
    {"FloatFromDouble", (void *)STD_MathC_FloatFromDouble},
    {"FloatFromByte", (void *)STD_MathC_FloatFromByte},
    {"FloatFromSByte", (void *)STD_MathC_FloatFromSByte},
    {"FloatFromChar", (void *)STD_MathC_FloatFromChar},
    {"FloatFromShort", (void *)STD_MathC_FloatFromShort},
    {"FloatFromUShort", (void *)STD_MathC_FloatFromUShort},

    {"DoubleFromBool", (void *)STD_MathC_DoubleFromBool},
    {"DoubleFromInt", (void *)STD_MathC_DoubleFromInt},
    {"DoubleFromUInt", (void *)STD_MathC_DoubleFromUInt},
    {"DoubleFromLong", (void *)STD_MathC_DoubleFromLong},
    {"DoubleFromULong", (void *)STD_MathC_DoubleFromULong},
    {"DoubleFromFloat", (void *)STD_MathC_DoubleFromFloat},
    {"DoubleFromDouble", (void *)STD_MathC_DoubleFromDouble},
    {"DoubleFromByte", (void *)STD_MathC_DoubleFromByte},
    {"DoubleFromSByte", (void *)STD_MathC_DoubleFromSByte},
    {"DoubleFromChar", (void *)STD_MathC_DoubleFromChar},
    {"DoubleFromShort", (void *)STD_MathC_DoubleFromShort},
    {"DoubleFromUShort", (void *)STD_MathC_DoubleFromUShort},

    {"ByteFromBool", (void *)STD_MathC_ByteFromBool},
    {"ByteFromInt", (void *)STD_MathC_ByteFromInt},
    {"ByteFromUInt", (void *)STD_MathC_ByteFromUInt},
    {"ByteFromLong", (void *)STD_MathC_ByteFromLong},
    {"ByteFromULong", (void *)STD_MathC_ByteFromULong},
    {"ByteFromFloat", (void *)STD_MathC_ByteFromFloat},
    {"ByteFromDouble", (void *)STD_MathC_ByteFromDouble},
    {"ByteFromByte", (void *)STD_MathC_ByteFromByte},
    {"ByteFromSByte", (void *)STD_MathC_ByteFromSByte},
    {"ByteFromChar", (void *)STD_MathC_ByteFromChar},
    {"ByteFromShort", (void *)STD_MathC_ByteFromShort},
    {"ByteFromUShort", (void *)STD_MathC_ByteFromUShort},

    {"SByteFromBool", (void *)STD_MathC_SByteFromBool},
    {"SByteFromInt", (void *)STD_MathC_SByteFromInt},
    {"SByteFromUInt", (void *)STD_MathC_SByteFromUInt},
    {"SByteFromLong", (void *)STD_MathC_SByteFromLong},
    {"SByteFromULong", (void *)STD_MathC_SByteFromULong},
    {"SByteFromFloat", (void *)STD_MathC_SByteFromFloat},
    {"SByteFromDouble", (void *)STD_MathC_SByteFromDouble},
    {"SByteFromByte", (void *)STD_MathC_SByteFromByte},
    {"SByteFromSByte", (void *)STD_MathC_SByteFromSByte},
    {"SByteFromChar", (void *)STD_MathC_SByteFromChar},
    {"SByteFromShort", (void *)STD_MathC_SByteFromShort},
    {"SByteFromUShort", (void *)STD_MathC_SByteFromUShort},

    {"CharFromBool", (void *)STD_MathC_CharFromBool},
    {"CharFromInt", (void *)STD_MathC_CharFromInt},
    {"CharFromUInt", (void *)STD_MathC_CharFromUInt},
    {"CharFromLong", (void *)STD_MathC_CharFromLong},
    {"CharFromULong", (void *)STD_MathC_CharFromULong},
    {"CharFromFloat", (void *)STD_MathC_CharFromFloat},
    {"CharFromDouble", (void *)STD_MathC_CharFromDouble},
    {"CharFromByte", (void *)STD_MathC_CharFromByte},
    {"CharFromSByte", (void *)STD_MathC_CharFromSByte},
    {"CharFromChar", (void *)STD_MathC_CharFromChar},
    {"CharFromShort", (void *)STD_MathC_CharFromShort},
    {"CharFromUShort", (void *)STD_MathC_CharFromUShort},

    {"ShortFromBool", (void *)STD_MathC_ShortFromBool},
    {"ShortFromInt", (void *)STD_MathC_ShortFromInt},
    {"ShortFromUInt", (void *)STD_MathC_ShortFromUInt},
    {"ShortFromLong", (void *)STD_MathC_ShortFromLong},
    {"ShortFromULong", (void *)STD_MathC_ShortFromULong},
    {"ShortFromFloat", (void *)STD_MathC_ShortFromFloat},
    {"ShortFromDouble", (void *)STD_MathC_ShortFromDouble},
    {"ShortFromByte", (void *)STD_MathC_ShortFromByte},
    {"ShortFromSByte", (void *)STD_MathC_ShortFromSByte},
    {"ShortFromChar", (void *)STD_MathC_ShortFromChar},
    {"ShortFromShort", (void *)STD_MathC_ShortFromShort},
    {"ShortFromUShort", (void *)STD_MathC_ShortFromUShort},

    {"UShortFromBool", (void *)STD_MathC_UShortFromBool},
    {"UShortFromInt", (void *)STD_MathC_UShortFromInt},
    {"UShortFromUInt", (void *)STD_MathC_UShortFromUInt},
    {"UShortFromLong", (void *)STD_MathC_UShortFromLong},
    {"UShortFromULong", (void *)STD_MathC_UShortFromULong},
    {"UShortFromFloat", (void *)STD_MathC_UShortFromFloat},
    {"UShortFromDouble", (void *)STD_MathC_UShortFromDouble},
    {"UShortFromByte", (void *)STD_MathC_UShortFromByte},
    {"UShortFromSByte", (void *)STD_MathC_UShortFromSByte},
    {"UShortFromChar", (void *)STD_MathC_UShortFromChar},
    {"UShortFromShort", (void *)STD_MathC_UShortFromShort},
    {"UShortFromUShort", (void *)STD_MathC_UShortFromUShort},

    {"ToBool", (void *)STD_MathC_BoolFromBool},
    {"ToBool", (void *)STD_MathC_BoolFromInt},
    {"ToBool", (void *)STD_MathC_BoolFromUInt},
    {"ToBool", (void *)STD_MathC_BoolFromLong},
    {"ToBool", (void *)STD_MathC_BoolFromULong},
    {"ToBool", (void *)STD_MathC_BoolFromFloat},
    {"ToBool", (void *)STD_MathC_BoolFromDouble},
    {"ToBool", (void *)STD_MathC_BoolFromByte},
    {"ToBool", (void *)STD_MathC_BoolFromSByte},
    {"ToBool", (void *)STD_MathC_BoolFromChar},
    {"ToBool", (void *)STD_MathC_BoolFromShort},
    {"ToBool", (void *)STD_MathC_BoolFromUShort},

    {"ToInt", (void *)STD_MathC_IntFromBool},
    {"ToInt", (void *)STD_MathC_IntFromInt},
    {"ToInt", (void *)STD_MathC_IntFromUInt},
    {"ToInt", (void *)STD_MathC_IntFromLong},
    {"ToInt", (void *)STD_MathC_IntFromULong},
    {"ToInt", (void *)STD_MathC_IntFromFloat},
    {"ToInt", (void *)STD_MathC_IntFromDouble},
    {"ToInt", (void *)STD_MathC_IntFromByte},
    {"ToInt", (void *)STD_MathC_IntFromSByte},
    {"ToInt", (void *)STD_MathC_IntFromChar},
    {"ToInt", (void *)STD_MathC_IntFromShort},
    {"ToInt", (void *)STD_MathC_IntFromUShort},

    {"ToUInt", (void *)STD_MathC_UIntFromBool},
    {"ToUInt", (void *)STD_MathC_UIntFromInt},
    {"ToUInt", (void *)STD_MathC_UIntFromUInt},
    {"ToUInt", (void *)STD_MathC_UIntFromLong},
    {"ToUInt", (void *)STD_MathC_UIntFromULong},
    {"ToUInt", (void *)STD_MathC_UIntFromFloat},
    {"ToUInt", (void *)STD_MathC_UIntFromDouble},
    {"ToUInt", (void *)STD_MathC_UIntFromByte},
    {"ToUInt", (void *)STD_MathC_UIntFromSByte},
    {"ToUInt", (void *)STD_MathC_UIntFromChar},
    {"ToUInt", (void *)STD_MathC_UIntFromShort},
    {"ToUInt", (void *)STD_MathC_UIntFromUShort},

    {"ToLong", (void *)STD_MathC_LongFromBool},
    {"ToLong", (void *)STD_MathC_LongFromInt},
    {"ToLong", (void *)STD_MathC_LongFromUInt},
    {"ToLong", (void *)STD_MathC_LongFromLong},
    {"ToLong", (void *)STD_MathC_LongFromULong},
    {"ToLong", (void *)STD_MathC_LongFromFloat},
    {"ToLong", (void *)STD_MathC_LongFromDouble},
    {"ToLong", (void *)STD_MathC_LongFromByte},
    {"ToLong", (void *)STD_MathC_LongFromSByte},
    {"ToLong", (void *)STD_MathC_LongFromChar},
    {"ToLong", (void *)STD_MathC_LongFromShort},
    {"ToLong", (void *)STD_MathC_LongFromUShort},

    {"ToULong", (void *)STD_MathC_ULongFromBool},
    {"ToULong", (void *)STD_MathC_ULongFromInt},
    {"ToULong", (void *)STD_MathC_ULongFromUInt},
    {"ToULong", (void *)STD_MathC_ULongFromLong},
    {"ToULong", (void *)STD_MathC_ULongFromULong},
    {"ToULong", (void *)STD_MathC_ULongFromFloat},
    {"ToULong", (void *)STD_MathC_ULongFromDouble},
    {"ToULong", (void *)STD_MathC_ULongFromByte},
    {"ToULong", (void *)STD_MathC_ULongFromSByte},
    {"ToULong", (void *)STD_MathC_ULongFromChar},
    {"ToULong", (void *)STD_MathC_ULongFromShort},
    {"ToULong", (void *)STD_MathC_ULongFromUShort},

    {"ToFloat", (void *)STD_MathC_FloatFromBool},
    {"ToFloat", (void *)STD_MathC_FloatFromInt},
    {"ToFloat", (void *)STD_MathC_FloatFromUInt},
    {"ToFloat", (void *)STD_MathC_FloatFromLong},
    {"ToFloat", (void *)STD_MathC_FloatFromULong},
    {"ToFloat", (void *)STD_MathC_FloatFromFloat},
    {"ToFloat", (void *)STD_MathC_FloatFromDouble},
    {"ToFloat", (void *)STD_MathC_FloatFromByte},
    {"ToFloat", (void *)STD_MathC_FloatFromSByte},
    {"ToFloat", (void *)STD_MathC_FloatFromChar},
    {"ToFloat", (void *)STD_MathC_FloatFromShort},
    {"ToFloat", (void *)STD_MathC_FloatFromUShort},

    {"ToDouble", (void *)STD_MathC_DoubleFromBool},
    {"ToDouble", (void *)STD_MathC_DoubleFromInt},
    {"ToDouble", (void *)STD_MathC_DoubleFromUInt},
    {"ToDouble", (void *)STD_MathC_DoubleFromLong},
    {"ToDouble", (void *)STD_MathC_DoubleFromULong},
    {"ToDouble", (void *)STD_MathC_DoubleFromFloat},
    {"ToDouble", (void *)STD_MathC_DoubleFromDouble},
    {"ToDouble", (void *)STD_MathC_DoubleFromByte},
    {"ToDouble", (void *)STD_MathC_DoubleFromSByte},
    {"ToDouble", (void *)STD_MathC_DoubleFromChar},
    {"ToDouble", (void *)STD_MathC_DoubleFromShort},
    {"ToDouble", (void *)STD_MathC_DoubleFromUShort},

    {"ToByte", (void *)STD_MathC_ByteFromBool},
    {"ToByte", (void *)STD_MathC_ByteFromInt},
    {"ToByte", (void *)STD_MathC_ByteFromUInt},
    {"ToByte", (void *)STD_MathC_ByteFromLong},
    {"ToByte", (void *)STD_MathC_ByteFromULong},
    {"ToByte", (void *)STD_MathC_ByteFromFloat},
    {"ToByte", (void *)STD_MathC_ByteFromDouble},
    {"ToByte", (void *)STD_MathC_ByteFromByte},
    {"ToByte", (void *)STD_MathC_ByteFromSByte},
    {"ToByte", (void *)STD_MathC_ByteFromChar},
    {"ToByte", (void *)STD_MathC_ByteFromShort},
    {"ToByte", (void *)STD_MathC_ByteFromUShort},

    {"ToSByte", (void *)STD_MathC_SByteFromBool},
    {"ToSByte", (void *)STD_MathC_SByteFromInt},
    {"ToSByte", (void *)STD_MathC_SByteFromUInt},
    {"ToSByte", (void *)STD_MathC_SByteFromLong},
    {"ToSByte", (void *)STD_MathC_SByteFromULong},
    {"ToSByte", (void *)STD_MathC_SByteFromFloat},
    {"ToSByte", (void *)STD_MathC_SByteFromDouble},
    {"ToSByte", (void *)STD_MathC_SByteFromByte},
    {"ToSByte", (void *)STD_MathC_SByteFromSByte},
    {"ToSByte", (void *)STD_MathC_SByteFromChar},
    {"ToSByte", (void *)STD_MathC_SByteFromShort},
    {"ToSByte", (void *)STD_MathC_SByteFromUShort},

    {"ToChar", (void *)STD_MathC_CharFromBool},
    {"ToChar", (void *)STD_MathC_CharFromInt},
    {"ToChar", (void *)STD_MathC_CharFromUInt},
    {"ToChar", (void *)STD_MathC_CharFromLong},
    {"ToChar", (void *)STD_MathC_CharFromULong},
    {"ToChar", (void *)STD_MathC_CharFromFloat},
    {"ToChar", (void *)STD_MathC_CharFromDouble},
    {"ToChar", (void *)STD_MathC_CharFromByte},
    {"ToChar", (void *)STD_MathC_CharFromSByte},
    {"ToChar", (void *)STD_MathC_CharFromChar},
    {"ToChar", (void *)STD_MathC_CharFromShort},
    {"ToChar", (void *)STD_MathC_CharFromUShort},

    {"ToShort", (void *)STD_MathC_ShortFromBool},
    {"ToShort", (void *)STD_MathC_ShortFromInt},
    {"ToShort", (void *)STD_MathC_ShortFromUInt},
    {"ToShort", (void *)STD_MathC_ShortFromLong},
    {"ToShort", (void *)STD_MathC_ShortFromULong},
    {"ToShort", (void *)STD_MathC_ShortFromFloat},
    {"ToShort", (void *)STD_MathC_ShortFromDouble},
    {"ToShort", (void *)STD_MathC_ShortFromByte},
    {"ToShort", (void *)STD_MathC_ShortFromSByte},
    {"ToShort", (void *)STD_MathC_ShortFromChar},
    {"ToShort", (void *)STD_MathC_ShortFromShort},
    {"ToShort", (void *)STD_MathC_ShortFromUShort},

    {"ToUShort", (void *)STD_MathC_UShortFromBool},
    {"ToUShort", (void *)STD_MathC_UShortFromInt},
    {"ToUShort", (void *)STD_MathC_UShortFromUInt},
    {"ToUShort", (void *)STD_MathC_UShortFromLong},
    {"ToUShort", (void *)STD_MathC_UShortFromULong},
    {"ToUShort", (void *)STD_MathC_UShortFromFloat},
    {"ToUShort", (void *)STD_MathC_UShortFromDouble},
    {"ToUShort", (void *)STD_MathC_UShortFromByte},
    {"ToUShort", (void *)STD_MathC_UShortFromSByte},
    {"ToUShort", (void *)STD_MathC_UShortFromChar},
    {"ToUShort", (void *)STD_MathC_UShortFromShort},
    {"ToUShort", (void *)STD_MathC_UShortFromUShort},

    {"ToString", (void *)STD_String_FromBool},
    {"ToString", (void *)STD_String_FromInt},
    {"ToString", (void *)STD_String_FromUInt},
    {"ToString", (void *)STD_String_FromLong},
    {"ToString", (void *)STD_String_FromULong},
    {"ToString", (void *)STD_String_FromFloat},
    {"ToString", (void *)STD_String_FromDouble},
    {"ToString", (void *)STD_String_FromByte},
    {"ToString", (void *)STD_String_FromSByte},
    {"ToString", (void *)STD_String_FromChar},
    {"ToString", (void *)STD_String_FromShort},
    {"ToString", (void *)STD_String_FromUShort},
};

static Definition definitions[] = {
    {
        .namespace_ = "STD",
        .name = "String",
        .methods = STD_String_methods,
        .method_count = (int)(sizeof(STD_String_methods) / sizeof(STD_String_methods[0])),
        .instance_size = sizeof(STD_String),
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = (InitFunc)new_STD_String,
        .free = (FreeFunc)free_STD_String,
        .show_refs = NULL,
    },
    {
        .namespace_ = "STD",
        .name = "Any",
        .methods = NULL,
        .method_count = 0,
        .instance_size = sizeof(STD_Any),
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = (InitFunc)new_STD_Any,
        .free = (FreeFunc)free_STD_Any,
        .show_refs = show_refs_STD_Any,
    },
    {
        .namespace_ = "STD",
        .name = "List",
        .methods = STD_List_methods,
        .method_count = (int)(sizeof(STD_List_methods) / sizeof(STD_List_methods[0])),
        .instance_size = sizeof(STD_List),
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = (InitFunc)new_STD_List,
        .free = (FreeFunc)free_STD_List,
        .show_refs = show_refs_STD_List,
    },
    {
        .namespace_ = "STD",
        .name = "STD",
        .methods = STD_STD_methods,
        .method_count = (int)(sizeof(STD_STD_methods) / sizeof(STD_STD_methods[0])),
        .instance_size = 0,
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = NULL,
        .free = NULL,
        .show_refs = NULL,
    },
    {
        .namespace_ = "STD",
        .name = "Math",
        .methods = STD_Math_methods,
        .method_count = (int)(sizeof(STD_Math_methods) / sizeof(STD_Math_methods[0])),
        .instance_size = 0,
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = NULL,
        .free = NULL,
        .show_refs = NULL,
    },
    {
        .namespace_ = "STD",
        .name = "MathF",
        .methods = STD_MathF_methods,
        .method_count = (int)(sizeof(STD_MathF_methods) / sizeof(STD_MathF_methods[0])),
        .instance_size = 0,
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = NULL,
        .free = NULL,
        .show_refs = NULL,
    },
    {
        .namespace_ = "STD",
        .name = "MathI",
        .methods = STD_MathI_methods,
        .method_count = (int)(sizeof(STD_MathI_methods) / sizeof(STD_MathI_methods[0])),
        .instance_size = 0,
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = NULL,
        .free = NULL,
        .show_refs = NULL,
    },
    {
        .namespace_ = "STD",
        .name = "MathC",
        .methods = STD_MathC_methods,
        .method_count = (int)(sizeof(STD_MathC_methods) / sizeof(STD_MathC_methods[0])),
        .instance_size = 0,
        .static_data = NULL,
        .show_static_refs = NULL,
        .new = NULL,
        .free = NULL,
        .show_refs = NULL,
    },
};

EXPORT void getDefinitions(APITable *table)
{
    table->count = (int)(sizeof(definitions) / sizeof(definitions[0]));
    table->defs = definitions;

    state = table->state;
    runtime_init = table->runtime_init;
    runtime_load_package = table->runtime_load_package;
    runtime_new = table->runtime_new;
    runtime_free = table->runtime_free;
    runtime_new_reference_local = table->runtime_new_reference_local;
    runtime_gc = table->runtime_gc;
    runtime_gc_force = table->runtime_gc_force;
    runtime_add_alloc = table->runtime_add_alloc;
    runtime_sub_alloc = table->runtime_sub_alloc;
    runtime_show_instance = table->runtime_show_instance;
    runtime_null_coalesce = table->runtime_null_coalesce;
    runtime_unwrap = table->runtime_unwrap;
    runtime_throw = table->runtime_throw;
    runtime_exception = table->runtime_exception;
}
