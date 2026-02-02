// dll.h
#pragma once
#include <stdlib.h>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

typedef struct DllHandle {
  HMODULE Module;
} DllHandle;

static inline DllHandle *dll_load(const char *path) {
  if (!path)
    return NULL;

  HMODULE m = LoadLibraryA(path);
  if (!m)
    return NULL;

  DllHandle *h = (DllHandle *)malloc(sizeof(DllHandle));
  if (!h) {
    FreeLibrary(m);
    return NULL;
  }

  h->Module = m;
  return h;
}

static inline void *dll_sym(DllHandle *dll, const char *symbol) {
  if (!dll || !dll->Module || !symbol)
    return NULL;
  return (void *)GetProcAddress(dll->Module, symbol);
}

static inline void dll_unload(DllHandle *dll) {
  if (!dll)
    return;
  if (dll->Module)
    FreeLibrary(dll->Module);
  free(dll);
}

#else
#include <dlfcn.h>

typedef struct DllHandle {
  void *Handle;
} DllHandle;

static inline DllHandle *dll_load(const char *path) {
  if (!path)
    return NULL;

  void *h = dlopen(path, RTLD_NOW);
  if (!h)
    return NULL;

  DllHandle *dll = (DllHandle *)malloc(sizeof(DllHandle));
  if (!dll) {
    dlclose(h);
    return NULL;
  }

  dll->Handle = h;
  return dll;
}

static inline void *dll_sym(DllHandle *dll, const char *symbol) {
  if (!dll || !dll->Handle || !symbol)
    return NULL;
  return dlsym(dll->Handle, symbol);
}

static inline void dll_unload(DllHandle *dll) {
  if (!dll)
    return;
  if (dll->Handle)
    dlclose(dll->Handle);
  free(dll);
}

#endif
