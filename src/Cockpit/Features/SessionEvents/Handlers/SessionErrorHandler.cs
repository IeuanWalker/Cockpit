using Cockpit.Extensions;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionErrorHandler
{
	internal static void Handle(SessionModel session, SessionErrorEvent evt)
	{
		if(evt.Data is null)
		{
			return;
		}

		ChatMessageModel message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Message ?? "An error occurred",
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageTypeEnum.Error,
			EventType = evt.Type,
			EventJson = [new Lazy<string>(() => SessionEventHelpers.SerializeEvent(evt))]
		};

		session.Messages.Add(message);
		session.Status = SessionStatusEnum.Error;
	}

	internal static void HandleException(SessionModel session, Exception ex)
	{
		ChatMessageModel message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = ex.Message,
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageTypeEnum.Error,
			EventJson = [new Lazy<string>(() => (session, ex).SerializeJson() ?? ex.StackTrace ?? string.Empty)]
		};

		session.Messages.Add(message);
		session.Status = SessionStatusEnum.Error;
	}
}
