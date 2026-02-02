#pragma once

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

static inline double time_ms(void) {
  static LARGE_INTEGER Freq;
  static int Init = 0;

  if (!Init) {
    QueryPerformanceFrequency(&Freq);
    Init = 1;
  }

  LARGE_INTEGER Counter;
  QueryPerformanceCounter(&Counter);

  return (double)Counter.QuadPart * 1000.0 / (double)Freq.QuadPart;
}

#else
#include <time.h>

static inline double time_ms(void) {
  struct timespec Ts;
#if defined(CLOCK_MONOTONIC)
  clock_gettime(CLOCK_MONOTONIC, &Ts);
#else
  clock_gettime(CLOCK_REALTIME, &Ts);
#endif
  return (double)Ts.tv_sec * 1000.0 + (double)Ts.tv_nsec / 1000000.0;
}
#endif
