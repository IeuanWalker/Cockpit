namespace Cockpit.Components.Pages.ChatPanel;

/// <summary>
/// Maintains a bounded, overlapping window over an append-oriented message list.
/// The UI owns viewport anchoring; this type only decides which indices are rendered.
/// </summary>
internal sealed class MessageWindowState(int maximumSize = 225, int shiftSize = 50)
{
	public int MaximumSize { get; } = maximumSize > 0
		? maximumSize
		: throw new ArgumentOutOfRangeException(nameof(maximumSize));

	public int ShiftSize { get; } = shiftSize > 0 && shiftSize < maximumSize
		? shiftSize
		: throw new ArgumentOutOfRangeException(nameof(shiftSize));

	public int StartIndex { get; private set; }
	public int EndIndex { get; private set; }
	public int TotalCount { get; private set; }
	public int UnseenMessageCount { get; private set; }
	public bool IncludesTail => EndIndex == TotalCount;
	public bool IsFollowingTail { get; private set; }
	public int Count => EndIndex - StartIndex;
	public bool CanMoveOlder => StartIndex > 0;
	public bool CanMoveNewer => EndIndex < TotalCount;

	public void Reset(int totalCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

		TotalCount = totalCount;
		EndIndex = totalCount;
		StartIndex = Math.Max(0, EndIndex - MaximumSize);
		UnseenMessageCount = 0;
		IsFollowingTail = true;
	}

	/// <summary>
	/// Synchronizes the window after the backing list changes. Appends follow the tail
	/// only while the user is already there; otherwise they contribute to the unseen count.
	/// Non-append structural changes are clamped defensively.
	/// </summary>
	public void Synchronize(int totalCount)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(totalCount);

		// An empty backing list is a reset boundary (for example, when a session is
		// cleared before its replay is repopulated). Keeping a historical browsing
		// position here would leave subsequent appends outside an empty window.
		if(totalCount == 0)
		{
			Reset(0);
			return;
		}

		bool followedTail = IsFollowingTail;
		int previousTotal = TotalCount;
		TotalCount = totalCount;

		if(totalCount >= previousTotal)
		{
			int added = totalCount - previousTotal;
			if(followedTail)
			{
				EndIndex = totalCount;
				StartIndex = Math.Max(0, EndIndex - MaximumSize);
				UnseenMessageCount = 0;
			}
			else
			{
				UnseenMessageCount += added;
			}
		}
		else
		{
			EndIndex = Math.Min(EndIndex, totalCount);
			StartIndex = Math.Min(StartIndex, Math.Max(0, EndIndex - 1));
			if(EndIndex - StartIndex > MaximumSize)
			{
				StartIndex = EndIndex - MaximumSize;
			}
			UnseenMessageCount = Math.Min(UnseenMessageCount, Math.Max(0, totalCount - EndIndex));
		}
	}

	public bool MoveOlder()
	{
		if(!CanMoveOlder)
		{
			return false;
		}

		StartIndex = Math.Max(0, StartIndex - ShiftSize);
		EndIndex = Math.Min(TotalCount, StartIndex + MaximumSize);
		IsFollowingTail = false;
		return true;
	}

	public bool MoveNewer()
	{
		if(!CanMoveNewer)
		{
			return false;
		}

		EndIndex = Math.Min(TotalCount, EndIndex + ShiftSize);
		StartIndex = Math.Max(0, EndIndex - MaximumSize);
		return true;
	}

	public void MoveToTail()
	{
		EndIndex = TotalCount;
		StartIndex = Math.Max(0, EndIndex - MaximumSize);
		UnseenMessageCount = 0;
		IsFollowingTail = true;
	}

	public void SetFollowingTail(bool following)
	{
		IsFollowingTail = following && IncludesTail;
		if(IsFollowingTail)
		{
			UnseenMessageCount = 0;
		}
	}
}

public sealed class MessageViewportAnchor
{
	public string? MessageId { get; set; }
	public double Offset { get; set; }
}

internal readonly record struct MessageWindowTailStateUpdate(long Generation, bool IncludesTail);

/// <summary>
/// Tracks the tail state last published to the JavaScript observer registration.
/// The generation is part of the identity because stale registrations reject updates.
/// </summary>
internal sealed class MessageWindowTailStateSync
{
	long? _publishedGeneration;
	bool _publishedIncludesTail;

	public bool TryCreateUpdate(
		bool observersReady,
		long generation,
		bool includesTail,
		out MessageWindowTailStateUpdate update)
	{
		if(!observersReady ||
			(_publishedGeneration == generation && _publishedIncludesTail == includesTail))
		{
			update = default;
			return false;
		}

		update = new(generation, includesTail);
		return true;
	}

	public void MarkPublished(MessageWindowTailStateUpdate update)
	{
		_publishedGeneration = update.Generation;
		_publishedIncludesTail = update.IncludesTail;
	}
}
