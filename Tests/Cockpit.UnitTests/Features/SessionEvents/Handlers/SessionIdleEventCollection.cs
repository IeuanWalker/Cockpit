namespace Cockpit.UnitTests.Features.SessionEvents.Handlers;

/// <summary>
/// Serializes all test classes that either subscribe to or fire <see cref="SessionIdleHandler.OnSessionFinished"/>.
/// Required because <c>OnSessionFinished</c> is a static event — parallel execution of classes that process
/// <c>SessionIdleEvent</c> would cause spurious event firings to interfere with subscription-based assertions.
/// </summary>
[CollectionDefinition("SessionIdleEvent")]
public class SessionIdleEventCollection;
