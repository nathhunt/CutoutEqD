// .NET Framework 4.8 build only: keep "Math" meaning System.Math in the app code.
#if !NET5_0_OR_GREATER
global using Math = System.Math;
#endif
