using System.Runtime.CompilerServices;

// Lets the test project reach internal test seams (e.g. ResurfacingService's
// RNG-injecting constructor) without widening the public API surface.
[assembly: InternalsVisibleTo("MemoryTimeline.Tests")]
