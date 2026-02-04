// main.c
#define STB_DS_IMPLEMENTATION
#define FUNCTION_SIG
#include "runtime.h"
#include "stb_ds.h"

EXPORT Instance *runtime_new(RuntimeState *state, const char *namespace_, const char *name)
{
    for (int i = 0; i < arrlen(state->definitions); i++)
    {
        Definition *def = state->definitions[i];
        if (strcmp(def->namespace_, namespace_) == 0 && strcmp(def->name, name) == 0)
        {
            Instance *inst = def->new();
            inst->definition = def;
            inst->seen = false;
            arrput(state->instances, inst);
            runtime_add_alloc(state, (size_t)def->instance_size);
            return inst;
        }
    }
    return NULL;
}

EXPORT ReferenceLocal runtime_new_reference_local(RuntimeState *state, Instance **instance)
{
    ReferenceLocal local = {0};
    local.instance = instance;
    local.prev = state->locals;
    return local;
}

EXPORT RuntimeState *runtime_init()
{
    RuntimeState *state = (RuntimeState *)malloc(sizeof(RuntimeState));
    if (!state)
        return NULL;

    state->definitions = NULL;
    state->locals = NULL;
    state->instances = NULL;
    state->dlls = NULL;
    state->allocated_bytes = 0;
    state->gc_threshold = 1024 * 1024;
    return state;
}
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#if defined(_WIN32)
#include <windows.h>
#else
#include <dirent.h>
#include <sys/stat.h>
#endif

static int has_suffix(const char *str, const char *suffix)
{
    size_t str_len = strlen(str);
    size_t suffix_len = strlen(suffix);

    if (str_len < suffix_len)
        return 0;

    return memcmp(str + str_len - suffix_len, suffix, suffix_len) == 0;
}

static int is_shared_lib_name(const char *name)
{
#if defined(_WIN32)
    return has_suffix(name, ".dll") || has_suffix(name, ".DLL");
#else
    if (has_suffix(name, ".so"))
        return 1;

    if (strstr(name, ".so.") != NULL)
        return 1;

    return 0;
#endif
}

static void strip_shared_lib_extension(const char *filename, char *out, size_t out_size)
{
    snprintf(out, out_size, "%s", filename);

#if defined(_WIN32)
    size_t len = strlen(out);
    if (len >= 4 && _stricmp(out + len - 4, ".dll") == 0)
        out[len - 4] = '\0';
#else
    char *so_pos = strstr(out, ".so");
    if (so_pos != NULL)
        *so_pos = '\0';
#endif
}

static void load_packages_from_folder(const char *folder_path, RuntimeState *state)
{
#if defined(_WIN32)
    char search_path[MAX_PATH];
    snprintf(search_path, sizeof(search_path), "%s\\*", folder_path);

    WIN32_FIND_DATAA find_data;
    HANDLE find = FindFirstFileA(search_path, &find_data);
    if (find == INVALID_HANDLE_VALUE)
        return;

    do
    {
        if (find_data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            continue;

        if (!is_shared_lib_name(find_data.cFileName))
            continue;

        char full_path[MAX_PATH];
        char no_ext_path[MAX_PATH];

        snprintf(full_path, sizeof(full_path), "%s\\%s", folder_path, find_data.cFileName);
        strip_shared_lib_extension(full_path, no_ext_path, sizeof(no_ext_path));

        if (!runtime_load_package(no_ext_path, state))
            printf("Package load failed: %s\n", no_ext_path);

    } while (FindNextFileA(find, &find_data));

    FindClose(find);

#else
    DIR *dir = opendir(folder_path);
    if (!dir)
        return;

    struct dirent *entry;
    while ((entry = readdir(dir)) != NULL)
    {
        if (entry->d_type == DT_DIR)
            continue;

        if (!is_shared_lib_name(entry->d_name))
            continue;

        char full_path[1024];
        char no_ext_path[1024];

        snprintf(full_path, sizeof(full_path), "%s/%s", folder_path, entry->d_name);
        strip_shared_lib_extension(full_path, no_ext_path, sizeof(no_ext_path));

        if (!runtime_load_package(no_ext_path, state))
            printf("Package load failed: %s\n", no_ext_path);
    }

    closedir(dir);
#endif
}

EXPORT void runtime_free(RuntimeState *state)
{
    if (!state)
        return;

    arrfree(state->definitions);
    state->definitions = NULL;
    unsigned long long cleaned = 0;
    for (int i = 0; i < arrlen(state->instances); i++)
    {
        Instance *inst = state->instances[i];
        if (inst)
        {
            Definition *def = inst->definition;
            runtime_sub_alloc(state, def->instance_size);
            if (def->free)
                def->free(inst);
            cleaned++;
        }
    }
    debugprintf("runtime free done %llu instances cleaned\n", cleaned);
    arrfree(state->instances);
    state->instances = NULL;
    state->locals = NULL;
    for (int i = 0; i < arrlen(state->dlls); i++)
    {
        DllHandle *dll = state->dlls[i];
        dll_unload(dll);
    }
    arrfree(state->dlls);
    state->dlls = NULL;

    free(state);
}

EXPORT bool runtime_load_package(const char *name, RuntimeState *state)
{
    if (!state)
        return false;

    char dllPath[512];
#if defined(_WIN32)
    snprintf(dllPath, sizeof(dllPath), "%s.dll", name);
#else
    snprintf(dllPath, sizeof(dllPath), "%s.so", name);
#endif

    DllHandle *dll = dll_load(dllPath);
    if (!dll)
    {
        printf("Failed to load dll %s\n", dllPath);
        return false;
    }
    void *getDefinitions = dll_sym(dll, "getDefinitions");
    if (!getDefinitions)
    {
        printf("Failed to load getDefinitions\n");
        return false;
    }
    APITable table;
    table.state = state;
    table.runtime_init = runtime_init;
    table.runtime_load_package = runtime_load_package;
    table.runtime_new = runtime_new;
    table.runtime_free = runtime_free;
    table.runtime_new_reference_local = runtime_new_reference_local;
    table.runtime_gc = runtime_gc;
    table.runtime_gc_force = runtime_gc_force;
    table.runtime_add_alloc = runtime_add_alloc;
    table.runtime_sub_alloc = runtime_sub_alloc;
    table.runtime_show_instance = runtime_show_instance;
    table.runtime_null_coalesce = runtime_null_coalesce;
    table.runtime_unwrap = runtime_unwrap;
    ((GetDefinitionsFunc)getDefinitions)(&table);

    for (int i = 0; i < table.count; i++)
    {
        arrput(state->definitions, &table.defs[i]);
    }

    arrput(state->dlls, dll);
    return true;
}

static Instance **gc_worklist = NULL;
static bool gc_epoch = false;

EXPORT void runtime_show_instance(Instance *instance)
{
    if (!instance)
        return;
    if (instance->seen == gc_epoch)
        return;
    arrput(gc_worklist, instance);
}

static double gc_time = 0;

static void runtime_gc_collect(RuntimeState *state)
{
    gc_epoch = !gc_epoch;
    double start = time_ms();
    ReferenceLocal *local = state->locals;
    while (local)
    {
        Instance *inst = *local->instance;
        local = local->prev;
        if (!inst)
            continue;
        arrput(gc_worklist, inst);
    }
    for (int i = 0; i < arrlen(state->definitions); i++)
    {
        Definition *def = state->definitions[i];
        if (def->show_static_refs)
            def->show_static_refs();
    }
    while (arrlen(gc_worklist) > 0)
    {
        Instance *inst = arrpop(gc_worklist);
        if (!inst)
            continue;
        inst->seen = gc_epoch;
        Definition *def = inst->definition;
        if (def->show_refs)
            def->show_refs(inst);
    }
    arrfree(gc_worklist);
    gc_worklist = NULL;
    unsigned long long cleaned = 0;
    for (int i = 0; i < arrlen(state->instances);)
    {
        Instance *inst = state->instances[i];
        if (inst && inst->seen == gc_epoch)
        {
            i++;
            continue;
        }
        if (inst)
        {
            Definition *def = inst->definition;
            runtime_sub_alloc(state, def->instance_size);
            if (def->free)
                def->free(inst);
            cleaned++;
        }
        int last = arrlen(state->instances) - 1;
        state->instances[i] = state->instances[last];
        arrpop(state->instances);
    }
    state->gc_threshold = state->allocated_bytes * 2;
    double end = time_ms();
    gc_time += end - start;
    debugprintf("GC done %llu instances cleaned\n", cleaned);
}

EXPORT void runtime_gc(RuntimeState *state)
{
    if (state->allocated_bytes <= state->gc_threshold)
        return;
    runtime_gc_collect(state);
}

EXPORT void runtime_gc_force(RuntimeState *state)
{
    runtime_gc_collect(state);
}

EXPORT void *runtime_null_coalesce(void *a, void *b)
{
    return a ? a : b;
}

EXPORT void *runtime_unwrap(void *a, int line)
{
    if (a)
        return a;
    printf("\ndamnit on nil value on line %d\n", line);
    abort();
    return NULL;
}

EXPORT void runtime_add_alloc(RuntimeState *state, size_t size)
{
    if (!state || size == 0)
        return;
    state->allocated_bytes += size;
}

EXPORT void runtime_sub_alloc(RuntimeState *state, size_t size)
{
    if (!state || size == 0)
        return;
    if (state->allocated_bytes < size)
        state->allocated_bytes = 0;
    else
        state->allocated_bytes -= size;
}

int main(int argc, char **argv)
{
    RuntimeState *state = runtime_init();
    if (!state)
    {
        printf("Failed to init runtime\n");
        return 1;
    }
    if (argc < 2)
    {
        printf("Usage: runtime <package folder>\n");
        return 1;
    }
    load_packages_from_folder(argv[1], state);

    for (int i = 0; i < arrlen(state->definitions); i++)
    {
        Definition *def = state->definitions[i];
        if (strcmp(def->name, "App") == 0)
            for (int j = 0; j < def->method_count; j++)
            {
                Method *m = &def->methods[j];
                if (strcmp(m->name, "Main") == 0)
                {
                    printf("Calling App::Main\n");
                    ((void (*)(void))m->entry)();
                    printf("App::Main returned\n");
                    goto exit;
                }
            }
    }

exit:
    runtime_free(state);
    printf("GC time: %f ms\n", gc_time);
    return 0;
}
