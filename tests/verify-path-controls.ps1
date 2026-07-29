$projectFile = Get-Content -Raw 'quickbackup\quickbackup.csproj'
$formFile = Get-Content -Raw 'quickbackup\Form1.cs'

$required = @(
    @{ Content = $projectFile; Text = '<Version>0.24</Version>' },
    @{ Content = $formFile; Text = 'private const string AppVersion = "0.24";' },
    @{ Content = $formFile; Text = 'Select Folder' },
    @{ Content = $formFile; Text = 'Open in Explorer' },
    @{ Content = $formFile; Text = 'Remove Backup Path' },
    @{ Content = $formFile; Text = 'Text = "O"' },
    @{ Content = $formFile; Text = 'Name = "openPathButton"' },
    @{ Content = $formFile; Text = 'button.Name == "openPathButton" || enabled' },
    @{ Content = $formFile; Text = 'using System.Diagnostics;' },
    @{ Content = $formFile; Text = 'Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })' },
    @{ Content = $formFile; Text = 'createMissingDirectories: true' },
    @{ Content = $formFile; Text = 'The folder does not exist:' },
    @{ Content = $formFile; Text = 'Directory.CreateDirectory(path)' }
)

foreach ($check in $required) {
    if (-not $check.Content.Contains($check.Text)) {
        throw "Required path-control code is missing: $($check.Text)"
    }
}
