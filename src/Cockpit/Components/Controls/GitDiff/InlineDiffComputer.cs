namespace Cockpit.Components.Controls.GitDiff;

internal static class InlineDiffComputer
{
	const double SimilarityThreshold = 0.3;

	static List<(string Token, int Start)> Tokenize(string text)
	{
		var tokens = new List<(string, int)>();
		int i = 0;
		while(i < text.Length)
		{
			int start = i;
			bool isWord = char.IsLetterOrDigit(text[i]) || text[i] == '_';
			while(i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_') == isWord)
				i++;
			tokens.Add((text[start..i], start));
		}
		return tokens;
	}

	static int[,] BuildLcsTable(List<(string Token, int Start)> a, List<(string Token, int Start)> b)
	{
		int m = a.Count, n = b.Count;
		int[,] dp = new int[m + 1, n + 1];
		for(int i = 1; i <= m; i++)
			for(int j = 1; j <= n; j++)
				dp[i, j] = a[i - 1].Token == b[j - 1].Token
					? dp[i - 1, j - 1] + 1
					: Math.Max(dp[i - 1, j], dp[i, j - 1]);
		return dp;
	}

	public static (List<(int Start, int Length)> LeftSpans, List<(int Start, int Length)> RightSpans)
		Compute(string left, string right)
	{
		if(string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
			return ([], []);

		List<(string Token, int Start)> tokL = Tokenize(left);
		List<(string Token, int Start)> tokR = Tokenize(right);
		int[,] dp = BuildLcsTable(tokL, tokR);

		var matchedL = new HashSet<int>();
		var matchedR = new HashSet<int>();
		int il = tokL.Count, ir = tokR.Count;
		while(il > 0 && ir > 0)
		{
			if(tokL[il - 1].Token == tokR[ir - 1].Token)
			{
				matchedL.Add(il - 1);
				matchedR.Add(ir - 1);
				il--;
				ir--;
			}
			else if(dp[il - 1, ir] >= dp[il, ir - 1])
				il--;
			else
				ir--;
		}

		int lcsChars = matchedL.Sum(i => tokL[i].Token.Length);
		int longerLen = Math.Max(left.Length, right.Length);
		if(longerLen > 0 && (double)lcsChars / longerLen < SimilarityThreshold)
			return ([], []);

		return (BuildSpans(tokL, matchedL, left.Length), BuildSpans(tokR, matchedR, right.Length));
	}

	static List<(int Start, int Length)> BuildSpans(
		List<(string Token, int Start)> tokens, HashSet<int> matched, int totalLength)
	{
		var spans = new List<(int Start, int Length)>();
		int i = 0;
		while(i < tokens.Count)
		{
			if(!matched.Contains(i))
			{
				int spanStart = tokens[i].Start;
				while(i < tokens.Count && !matched.Contains(i))
					i++;
				int spanEnd = i < tokens.Count ? tokens[i].Start : totalLength;
				spans.Add((spanStart, spanEnd - spanStart));
			}
			else
			{
				i++;
			}
		}
		return spans;
	}
}
