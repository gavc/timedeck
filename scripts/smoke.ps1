param([string]$Executable = (Join-Path $PSScriptRoot '..\artifacts\TimeDeck\TimeDeck.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TimeDeckWindowCapture {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
'@
$Executable = [IO.Path]::GetFullPath($Executable)
if (Get-Process TimeDeck -ErrorAction SilentlyContinue) { throw 'Exit the existing Time Deck instance before running this isolated smoke check.' }
$artifactDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts'))
$dataDirectory = Join-Path $artifactDirectory ('smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$stateFile = Join-Path $dataDirectory 'state.json'
$script:appProcess = $null
$script:root = $null
function Assert-Check($condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    Write-Output "PASS: $message"
}
function Find-Element($parent, [string]$property, [string]$value) {
    $id = switch ($property) {
        'Id' { [System.Windows.Automation.AutomationElement]::AutomationIdProperty }
        default { [System.Windows.Automation.AutomationElement]::NameProperty }
    }
    $condition = [System.Windows.Automation.PropertyCondition]::new($id, $value)
    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-Element($parent, [string]$property, [string]$value) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $found = Find-Element $parent $property $value
        if ($found) { return $found }
        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $deadline)
    throw "Timed out finding $property=$value"
}
function Invoke-Element($element) {
    ([System.Windows.Automation.InvokePattern]$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 350
}
function Click([string]$property, [string]$value) { Invoke-Element (Wait-Element $script:root $property $value) }
function Set-Field($parent, [string]$name, [string]$value) {
    $null = Wait-Element $parent 'Name' $name
    $matches = $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    foreach ($field in $matches) {
        $pattern = $null
        if ($field.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
            ([System.Windows.Automation.ValuePattern]$pattern).SetValue($value)
            return
        }
    }
    throw "No editable field named $name"
}
function Read-State {
    $deadline = (Get-Date).AddSeconds(3)
    do {
        try { return [IO.File]::ReadAllText($stateFile) | ConvertFrom-Json }
        catch [IO.IOException] { Start-Sleep -Milliseconds 50 }
    } while ((Get-Date) -lt $deadline)
    throw 'Could not read persisted state after an atomic replacement.'
}
function Wait-State([scriptblock]$predicate) {
    $deadline = (Get-Date).AddSeconds(20)
    do {
        $current = Read-State
        if (& $predicate $current) { return $current }
        Start-Sleep -Milliseconds 150
    } while ((Get-Date) -lt $deadline)
    throw 'Timed out waiting for persisted state.'
}
function Start-App {
    $script:appProcess = Start-Process -FilePath $Executable -ArgumentList '--data-dir', $dataDirectory -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(25)
    do {
        $script:root = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:appProcess.Id))
        if ($script:root -and $script:root.Current.Name -eq 'Time Deck') { return }
        if ($script:appProcess.HasExited) { throw "Time Deck exited with $($script:appProcess.ExitCode)" }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    throw 'Time Deck did not open.'
}
function Stop-TestProcess {
    # This intentionally simulates a crash, using only the process started by this script.
    if ($script:appProcess -and -not $script:appProcess.HasExited) {
        Stop-Process -Id $script:appProcess.Id
        $script:appProcess.WaitForExit()
    }
}
function Capture-Window($element, [string]$filename) {
    $bounds = $element.Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    try {
        if (-not [TimeDeckWindowCapture]::PrintWindow([IntPtr]$element.Current.NativeWindowHandle, $hdc, 2)) { throw 'PrintWindow failed.' }
    } finally { $graphics.ReleaseHdc($hdc) }
    $bitmap.Save((Join-Path $artifactDirectory $filename))
    $graphics.Dispose()
    $bitmap.Dispose()
}
$today = [DateTimeOffset]::Now.Date
$sample = @{
    version = 1
    activities = @(@{ id = '11111111-1111-1111-1111-111111111111'; slot = 0; name = 'Smoke activity'; icon = '*'; color = '#2F7F6A'; category = 'Work' })
    entries = @(@{ id = '22222222-2222-2222-2222-222222222222'; activityId = '11111111-1111-1111-1111-111111111111'; label = 'Smoke activity'; start = ([DateTimeOffset]$today.AddHours(9)).ToUniversalTime().ToString('o'); end = ([DateTimeOffset]$today.AddHours(10)).ToUniversalTime().ToString('o') })
    settings = @{ workStart = '06:00:00'; workEnd = '18:00:00'; autoStop = $false; reminders = $true; reminderMinutes = 60; startWithWindows = $false; trayExplained = $true }
    running = $null
}
$sample | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $stateFile -Encoding UTF8
try {
    Start-App
    $all = $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $slots = @($all | Where-Object { $_.Current.AutomationId -match '^Slot\d+$' })
    Assert-Check ($slots.Count -eq 32) 'Exactly 32 deck positions'
    Assert-Check ((Find-Element $script:root 'Id' 'Slot30').Current.Name -like '*Report') 'Report fixed at slot 30'
    Assert-Check ((Find-Element $script:root 'Id' 'Slot31').Current.Name -like '*Edit') 'Edit fixed at slot 31'
    Assert-Check ((Find-Element $script:root 'Id' 'Slot32').Current.Name -like '*Settings') 'Settings fixed at slot 32'
    Click 'Id' 'Slot1'
    $null = Wait-State { param($s) $null -ne $s.running }
    Click 'Id' 'Slot1'
    $saved = Wait-State { param($s) $null -eq $s.running -and $s.entries.Count -eq 2 }
    Assert-Check ($saved.entries.Count -eq 2) 'Start and stop persist a completed entry'
    Click 'Id' 'Slot30'
    $report = Wait-Element $script:root 'Name' 'Time Deck report'
    $null = Wait-Element $report 'Name' 'Your time, clearly'
    Capture-Window $report 'report-preview.png'
    Click 'Name' 'Add time'
    $entry = Wait-Element $script:root 'Name' 'Add time'
    # Window and button share a name; locate the field from the owning application tree.
    Set-Field $script:root 'Activity' 'Smoke activity'
    Set-Field $script:root 'Start time · 24-hour time' '11:00:00'
    Set-Field $script:root 'End time · 24-hour time' '12:00:00'
    Click 'Name' 'Save entry'
    $null = Wait-State { param($s) $s.entries.Count -eq 3 }
    Assert-Check $true 'Report manual entry saves'
    ([System.Windows.Automation.WindowPattern]$report.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
    Click 'Id' 'Slot31'
    Click 'Id' 'Slot1'
    Set-Field $script:root 'Activity name' 'Renamed activity'
    $destination = $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Destination slot'),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)))
    ([System.Windows.Automation.ExpandCollapsePattern]$destination.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    $null = Wait-Element $script:root 'Name' 'Slot 2  ·  row 1, column 2'
    $targets = $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Slot 2  ·  row 1, column 2'))
    $selectedDestination = $false
    foreach ($target in $targets) {
        $selection = $null
        if ($target.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) {
            ([System.Windows.Automation.SelectionItemPattern]$selection).Select()
            $selectedDestination = $true
            break
        }
    }
    if (-not $selectedDestination) { throw 'Destination slot did not expose a selectable item.' }
    Click 'Name' 'Save activity'
    $renamed = Wait-State { param($s) $s.activities[0].name -eq 'Renamed activity' }
    Assert-Check (@($renamed.entries | Where-Object { $_.label -ne 'Renamed activity' }).Count -eq 0) 'Activity rename updates linked history'
    Assert-Check ($renamed.activities[0].slot -eq 1) 'Destination picker moves activity into a fixed empty slot'
    Click 'Id' 'Slot31'
    Click 'Id' 'Slot32'
    $settings = Wait-Element $script:root 'Name' 'Time Deck settings'
    Click 'Name' 'Test notification'
    $notificationStatus = $settings.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -like 'Notification sent to Windows*' }
    Assert-Check ($null -ne $notificationStatus) 'Windows accepts a native test notification'
    Set-Field $settings 'Reminder interval · minutes' '1'
    Click 'Name' 'Save settings'
    $null = Wait-State { param($s) $s.settings.reminderMinutes -eq 1 }
    Click 'Id' 'Slot2'
    ([System.Windows.Automation.WindowPattern]$script:root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
    Start-Sleep -Milliseconds 500
    Assert-Check (-not $script:appProcess.HasExited -and $null -ne (Read-State).running) 'Closing the deck keeps the process and timer alive'
    $second = Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru
    Assert-Check ($second.WaitForExit(10000)) 'Second launch activates existing instance and exits'
    Start-Sleep -Milliseconds 700
    Assert-Check (-not $script:root.Current.IsOffscreen) 'Existing deck reopens'
    Stop-TestProcess
    $recovery = Read-State
    $recovery.running.nextReminder = [DateTimeOffset]::UtcNow.AddSeconds(-1).ToString('o')
    $recovery | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $stateFile -Encoding UTF8
    Start-App
    $null = Wait-State { param($s) $s.running.reminderPending -eq $true }
    $null = Wait-Element $script:root 'Name' 'Still working'
    Assert-Check $true 'Restart recovers timer and presents an overdue check-in'
    Click 'Name' 'Still working'
    $ack = Wait-State { param($s) $s.running.reminderPending -eq $false }
    Assert-Check ([DateTimeOffset]::Parse($ack.running.nextReminder) -gt [DateTimeOffset]::UtcNow.AddSeconds(40)) 'Still working resets reminder without stopping'
    Capture-Window $script:root 'deck-preview.png'
    Stop-TestProcess
    $cutoffState = Read-State
    $cutoff = [DateTimeOffset]::UtcNow.AddSeconds(8)
    $cutoffState.running.cutoff = $cutoff.ToString('o')
    $cutoffState | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $stateFile -Encoding UTF8
    Start-App
    ([System.Windows.Automation.WindowPattern]$script:root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
    $stopped = Wait-State { param($s) $null -eq $s.running }
    Assert-Check ([DateTimeOffset]::Parse($stopped.entries[-1].end) -eq $cutoff) 'Hidden app stops at its exact persisted cutoff'
    Write-Output "Smoke data: $dataDirectory"
} finally { Stop-TestProcess }
