using System.Runtime.CompilerServices;

// The factory's contention test observes the authority gate through an internal seam, so it measures
// the real semaphore rather than a synthetic lock.
[assembly: InternalsVisibleTo("Wip.Factory.Agents.Tests")]