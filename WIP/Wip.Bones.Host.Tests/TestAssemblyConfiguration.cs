using Xunit;

// These tests start a full Bones web host (Kestrel + learning loop + stub provider) and wait on
// live status/frame timing. Running ~10 of those hosts concurrently on a small CI runner starves
// the waits and produces timing flakes unrelated to the code (CI run 41834133205: readCount==0,
// CI run 41836821034: ViewerUrl wait). Run the assembly's collections sequentially so one host
// is active at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]