using Cockpit.Features.Permissions;

namespace Cockpit.UnitTests.Features.Permissions;

public class CommandExtractorTests
{
	[Theory]
	[MemberData(nameof(GetNearestOptionsValidTests))]
	public async Task ExtractExecutables_CorrectCommands(int id, string command)
	{
		// Act
		List<string> result = CommandExtractor.ExtractExecutables(command);

		// Assert
		await Verify(result)
			.UseParameters(id);
	}

	public static TheoryData<int, string> GetNearestOptionsValidTests()
	{
		TheoryData<int, string> commands = [];

		commands.Add(1,
		"""
		$file = "D:\WS\Github-Mine\Cockpit\Cockpit.UnitTests.cs\CommandExtractorTests.cs"
		$lines = Get-Content $file
		for ($i = 0; $i -lt $lines.Count - 1; $i++) {
			if ($lines[$i].Trim() -eq '{' -and $lines[$i+1].Trim() -eq '{') {
			    Write-Host "Duplicate opening brace found at lines $($i+1) and $($i+2)"
			}
		}
		""");

		commands.Add(2,
		"""
		$file = "D:\WS\Github-Mine\Cockpit\Cockpit.UnitTests.cs\CommandExtractorTests.cs"
		$content = Get-Content $file -Raw

		# Step 1: Remove all duplicate opening braces (consecutive { on separate lines)
		$content = $content -replace '(\{)\s*\r?\n\s*\{', '$1'

		# Step 2: Fix IsDestructiveExecutable tests - they need Act and Assert sections
		# Pattern: IsDestructiveExecutable test with only Arrange, missing Act/Assert
		$isDestructivePattern = '(?s)(\[Fact\]\s+public void IsDestructiveExecutable_\w+_ReturnsTrue\(\)\s*\{\s*// Arrange\s*string command = "([^"]+)";)\s*\r?\n\s*// Assert\s*\}'

		$content = [regex]::Replace($content, $isDestructivePattern, {
		    param($match)
		    $fullMatch = $match.Groups[1].Value
		    $commandValue = $match.Groups[2].Value
		    return "$fullMatch`r`n`r`n`t`t// Act`r`n`t`tbool result = CommandExtractor.IsDestructiveExecutable(command);`r`n`r`n`t`t// Assert`r`n`t`tresult.ShouldBeTrue();`r`n`t}"
		})

		# Step 3: Fix IsDestructiveExecutable_Safe tests (ReturnsFalse)
		$isSafePattern = '(?s)(\[Fact\]\s+public void IsDestructiveExecutable_\w+_ReturnsFalse\(\)\s*\{\s*// Arrange\s*string command = "([^"]+)";)\s*\r?\n\s*// Assert\s*\}'

		$content = [regex]::Replace($content, $isSafePattern, {
		    param($match)
		    $fullMatch = $match.Groups[1].Value
		    $commandValue = $match.Groups[2].Value
		    return "$fullMatch`r`n`r`n`t`t// Act`r`n`t`tbool result = CommandExtractor.IsDestructiveExecutable(command);`r`n`r`n`t`t// Assert`r`n`t`tresult.ShouldBeFalse();`r`n`t}"
		})

		Set-Content $file $content -NoNewline
		Write-Host "Fixed CommandExtractorTests.cs"
		""");

		commands.Add(3,
		"""
		cd D:\WS\Github-Mine\Cockpit; git log --all --full-history -- "**/CommandExtractorTests.cs" --oneline | Select-Object -First 5
		""");

		commands.Add(4,
		"""
		# Get the exact content around these lines
		$file = "D:\WS\Github-Mine\Cockpit\Cockpit.UnitTests.cs\CommandExtractorTests.cs"
		$lines = Get-Content $file
		for ($i = 1624; $i -le 1628; $i++) {
		    Write-Host "$i`: $($lines[$i-1])"
		}
		""");

		commands.Add(5,
		"""
		csc /t:library /nologo /out:nul Cockpit.UnitTests.cs\CommandExtractorTests.cs Cockpit.UnitTests.cs\CommandExtractorUnicodeTests.cs 2>&1 | Select-Object -First 20
		""");

		commands.Add(6,
		"""
		cd Cockpit.UnitTests.cs
		git log --all --oneline -- CommandExtractorTests.cs 2>&1 | Select-Object -First 5
		""");

		commands.Add(7,
		"""
		# Create backup
		Copy-Item Cockpit.UnitTests.cs Cockpit.UnitTests.cs.backup -Recurse -Force
		Write-Host "Created backup" -ForegroundColor Green

		# Remove test project since it's corrupted
		Remove-Item Cockpit.UnitTests.cs -Recurse -Force
		Write-Host "Removed corrupted test project" -ForegroundColor Yellow
		""");

		commands.Add(8,
		"""
		cd \u0027D:\\WS\\Github-Mine\\Cockpit\u0027 \u0026\u0026 dotnet build -c Release --verbose 2\u003E\u00261 | tee build_output.log
		""");

		commands.Add(9,
		"""
		dotnet build src\\Cockpit\\Cockpit.csproj
		""");

		commands.Add(10,
		"""
		Get-Content "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\CommandExtractorTests.ExtractExecutables_CorrectCommands_id=2.verified.txt" | Format-Hex
		""");

		commands.Add(11,
		"""
		Get-ChildItem "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\CommandExtractorTests.ExtractExecutables_CorrectCommands_*.verified.txt" | ForEach-Object {
		    $content = Get-Content $_.FullName -Raw
		    "$($_.Name): Length=$($content.Length)"
		}
		""");

		commands.Add(12,
		"""
		# Create a truly empty file (like the verified one)
		Set-Content "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\CommandExtractorTests.ExtractExecutables_CorrectCommands_id=2.verified.txt" -Value $null -NoNewline
		# Check the new size
		(Get-Item "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\CommandExtractorTests.ExtractExecutables_CorrectCommands_id=2.verified.txt").Length
		""");

		commands.Add(13,
		"""
		Remove-Item "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\*.received.txt" -ErrorAction SilentlyContinue; Get-ChildItem "D:\WS\Github-Mine\Cockpit\Tests\Cockpit.UnitTests\Features\Permissions\*.received.txt" -ErrorAction SilentlyContinue | Measure-Object | Select-Object -ExpandProperty Count
		""");

		commands.Add(14,
		"""
		ls -la
		""");

		commands.Add(15,
		"""
		ls -la | grep test
		""");

		commands.Add(16,
		"""
		cd /tmp && ls -la
		""");

		commands.Add(17,
		"""
		echo hello; pwd
		""");

		commands.Add(18,
		"""
		echo hello\nls -la\npwd
		""");

		commands.Add(19,
		"""
		git add .
		""");

		commands.Add(20,
		"""
		git add . && git commit -m "test"
		""");

		commands.Add(21,
		"""
		npm install lodash
		""");

		commands.Add(22,
		"""
		docker run -it ubuntu
		""");

		commands.Add(23,
		"""
		gh copilot --help
		""");

		commands.Add(24,
		"""
		gh issue list
		""");

		commands.Add(25,
		"""
		gh pr create --title "test"
		""");

		commands.Add(26,
		"""
		gh auth login
		""");

		commands.Add(27,
		"""
		gh auth login && gh issue list
		""");

		commands.Add(28,
		"""
		export PATH="/usr/bin:$PATH"
		""");

		commands.Add(29,
		"""
		FOO=bar ls
		""");

		commands.Add(30,
		"""
		/usr/bin/ls -la'
		""");

		commands.Add(31,
		"""
		sudo apt-get install vim
		""");

		commands.Add(32,
		"""
		env VAR=1 python script.py
		""");

		commands.Add(33,
		"""
		cat > /tmp/file << 'EOF'
		content here
		more content
		EOF
		node script.js
		""");

		commands.Add(34,
		"""
		cat > /tmp/file <<"MARKER"
		some content
		MARKER
		next-command
		""");

		commands.Add(35,
		"""
		cat > /tmp/file <<EOF
		content
		EOF
		next
		""");

		commands.Add(36,
		"""
		cat > /tmp/file << EOF
		content
		EOF
		next
		""");

		commands.Add(37,
		"""
		cat <<EOF
		content
		EOF
		next
		""");

		commands.Add(38,
		"""
		export PATH="/opt/homebrew/opt/node.js/bin:/opt/homebrew/bin:$PATH" && cd /Users/idofrizler/Git/openwork && cat > /tmp/test-azure.mjs << 'EOF'
		// Test Azure OpenAI API directly
		const endpoint = 'https://.openai.azure.com';
		const apiKey = '';
		const deployments = ['gpt-5-chat', 'gpt-5.2-chat', 'DeepSeek-V3.1'];

		async function testDeployment(deployment) {
		const url = endpoint + '/openai/deployments/' + deployment;
		try {
		const response = await fetch(url, {
		method: 'POST',
		headers: {
		'Content-Type': 'application/json',
		'api-key': apiKey,
		},
		body: JSON.stringify({
		messages: [{ role: 'user', content: 'Say hello in 5 words' }],
		max_tokens: 50,
		}),
		});
		const data = await response.json();
		if (data.error) {
		console.log('error: ' + data.error.message);
		} else {
		console.log('success: ' + data.choices[0].message.content);
		}
		} catch (e) {
		console.log('error: ' + e.message);
		}
		}

		for (const d of deployments) {
		await testDeployment(d);
		}
		EOF
		node /tmp/test-azure.mjs
		""");

		commands.Add(39,
		"""
		cat > /tmp/file << 'EOF'
		const x = 1;
		async function test() {}
		""");

		commands.Add(40,
		"""
		cat > /tmp/a <<'A'
		content a
		A
		cat > /tmp/b <<'B'
		content b
		B
		echo done
		""");

		commands.Add(41,
		"""
		# Get the position of the window and calculate = button position
		# Standard calculator layout: = is bottom right
		osascript -e 'tell application "System Events" to tell process "Calculator" to get position of window 1'
		osascript -e 'tell application "System Events" to tell process "Calculator" to get size of window 1'
		""");

		commands.Add(42,
		"""
		ls -la # list files
		""");

		commands.Add(43,
		"""
		echo hello # print greeting
		""");

		commands.Add(44,
		"""
		# first comment
		ls -la
		# second comment
		pwd
		""");

		commands.Add(45,
		"""
		echo "test #123"
		""");

		commands.Add(45,
		"""
		git commit -m "fix #456"
		""");

		commands.Add(46,
		"""
		echo '#hashtag'
		""");

		commands.Add(46,
		"""
		echo "hello world"
		""");

		commands.Add(47,
		"""
		echo 'hello world'
		""");

		commands.Add(48,
		"""
		echo `date`
		""");

		commands.Add(49,
		"""
		echo hello > file.txt
		""");

		commands.Add(50,
		"""
		command 2>/dev/null | cat
		""");

		commands.Add(51,
		"""
		command 2>&1 && echo done
		""");

		commands.Add(52,
		"""
		which node npm 2>&1 || echo "not found"; cat /tmp/.nvmrc 2>&1 || true
		""");

		commands.Add(53,
		"""
		ls -la && false
		""");

		commands.Add(54,
		"""
		ls -la || true
		""");

		commands.Add(55,
		"""
		for f in ~/.copilot/logs/*.log; do echo "=== $f ==="; head -30 "$f" | grep -i "cwd" || true; done
		""");

		commands.Add(56,
		"""
		for f in $(ls -t ~/.copilot/session-state/*/events.jsonl 2>/dev/null | head -5); do echo "=== $f ==="; grep "compaction" "$f" 2>/dev/null | tail -3; done
		""");

		commands.Add(57,
		"""
		for f in $(ls -t ~/.copilot/session-state/*/events.jsonl 2>/dev/null | head -10); do
		compaction=$(grep -c "compaction" "$f" 2>/dev/null || echo 0)
		if [ "$compaction" -gt "0" ]; then
		echo "=== $f (compactions: $compaction) ==="
		grep "compaction_complete" "$f" 2>/dev/null | jq -r '.data.success // "N/A"' 2>/dev/null | sort | uniq -c
		fi
		done
		""");

		commands.Add(58,
		"""
		for f in $(ls -t ~/.copilot/session-state/*/events.jsonl 2>/dev/null | head -20); do
		  # Find compaction with success: null (not true)
		  if grep -q '"session.compaction_complete".*"success":null' "$f" 2>/dev/null; then
		    session=$(basename $(dirname "$f"))
		    echo "=== $session has null compaction ==="
		    # Show last few events
		    tail -5 "$f" | jq -r '.type' 2>/dev/null | head -5
		  fi
		done
		""");

		commands.Add(59,
		"""
		if test -f file.txt; then cat file.txt; else echo missing; fi
		""");

		commands.Add(60,
		"""
		for i in 1 2 3 4 5; do node /tmp/bot-social.js 2>/dev/null | tail -3; echo "---"; done
		""");

		commands.Add(61,
		"""
		cd /Users/idofrizler/temp && node server.js &
		sleep 2
		for i in 1 2 3; do
		  echo "=== Bot $i ==="
		  node /tmp/bot-social.js 2>&1 | grep -E "(TASK|Topic)"
		done
		""");

		commands.Add(62,
		"""
		curl -s -X POST http://localhost:3001/api/start | jq -r '.sessionId'
		""");

		commands.Add(63,
		"""
		az cosmosdb show --name nha-cosmos-db --resource-group no-humans-allowed-rg -o json
		""");

		commands.Add(64,
		"""
		az appservice plan delete --name no-humans-allowed-plan --resource-group no-humans-allowed-rg --yes
		""");

		commands.Add(65,
		"""
		curl -s -X POST http://localhost:3001/api/verify/\$SESSION -H "Content-Type: application/json" -d "{\\"key\\":\\"value\\"}" | jq '{success,level}'
		""");

		commands.Add(66,
		"""
		az webapp delete --name no-humans-allowed-app --resource-group no-humans-allowed-rg --subscription 74226166-2d6e-48b3-9194-6d3ef0c7bdff
		""");

		commands.Add(66,
		"""
		az webapp delete --name no-humans-allowed-app --resource-group no-humans-allowed-rg --subscription 74226166-2d6e-48b3-9194-6d3ef0c7bdff
		""");

		return commands;
	}
}