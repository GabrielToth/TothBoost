using Xunit;

// WPF exposes a single Application/Dispatcher per AppDomain. Running UI tests in
// parallel makes them race for that singleton, so the whole suite is serialized.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
