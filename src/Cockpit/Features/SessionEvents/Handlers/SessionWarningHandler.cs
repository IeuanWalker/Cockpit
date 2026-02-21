using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents.Handlers;

static class SessionWarningHandler
{
	internal static void Handle(ChatSession session, SessionWarningEvent evt, ILogger logger)
	{
		logger.LogWarning("Session {SessionId} warning [{WarningType}]: {Message}",
			session.Id, evt.Data.WarningType, evt.Data.Message);

		ChatMessageModel message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Message,
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageTypeEnum.Error,
			EventType = evt.Type
		};

		session.Messages.Add(message);
	}
}
