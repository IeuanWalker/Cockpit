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

		return commands;
	}
}