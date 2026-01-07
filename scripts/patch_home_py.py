import os

file_path = r"d:\prj\RTest\RTest\src\Radio.Web\Components\Pages\Home.razor"

with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

# Fix 1: Image Style
old_image_style = 'style="width: 400px; height: 400px; object-fit: cover; border-radius: 8px;"'
new_image_style = 'style="max-width: 300px; max-height: 300px; width: 100%; height: auto; object-fit: contain; border-radius: 8px;"'

if old_image_style in content:
    content = content.replace(old_image_style, new_image_style)
    print("Fixed Image Style")
else:
    print("Image Style pattern not found")

# Fix 2: Progress Bar -> Slider & Layout
# We need to replace the entire Progress Bar block
old_progress_block = """  <!-- Progress Bar -->
  @if (!string.IsNullOrEmpty(_position) && !string.IsNullOrEmpty(_duration))
  {
    <div style="margin-top: 32px; width: 100%; max-width: 600px;">
      <MudProgressLinear Color="Color.Primary" Size="Size.Medium" Value="@_progressPercent" Style="height: 8px; border-radius: 4px;" />   
      <div style="display: flex; justify-content: space-between; margin-top: 8px; font-size: 14px; color: var(--text-secondary);">        
        <span>@_position</span>
        <span>@_duration</span>
      </div>
    </div>
  }"""

new_progress_block = """  <!-- Progress Bar & Seeking -->
  @if (!string.IsNullOrEmpty(_duration))
  {
    <div style="margin-top: 32px; width: 100%; max-width: 600px;">
      <MudSlider T="double" Value="@_currentPositionSeconds" ValueChanged="@HandleSeekAsync" 
                 Min="0" Max="@_totalDurationSeconds" 
                 Color="Color.Primary" Size="Size.Medium" 
                 Immediate="false" />
      <div style="display: flex; justify-content: space-between; margin-top: -8px; font-size: 14px; color: var(--text-secondary);">
        <span>@(_currentPositionString ?? "00:00")</span>
        <span>@(_totalDurationString ?? "00:00")</span>
      </div>
    </div>
  }"""

# Normalizing line endings for search
content_normalized = content.replace('\r\n', '\n')
old_progress_block_normalized = old_progress_block.replace('\r\n', '\n')

if old_progress_block_normalized in content_normalized:
    content_normalized = content_normalized.replace(old_progress_block_normalized, new_progress_block)
    content = content_normalized # simplistic, might mess line endings but razor handles it
    print("Fixed Progress Bar")
else:
    print("Progress Bar pattern not found. Trying stricter search...")
    # The read content might have different spacing.
    # Let's try to match by parts.

# Fix 3: Add supporting C# code
# We need to add HandleSeekAsync, _currentPositionSeconds, updating logic.
# We'll inject it before "@code {" end
# Actually, inside @code block

code_injection = """
  // Seeking Support
  private double _currentPositionSeconds = 0;
  private double _totalDurationSeconds = 1;
  private string? _currentPositionString;
  private string? _totalDurationString;
  private System.Threading.Timer? _localProgressTimer;

  private async Task HandleSeekAsync(double newPosition)
  {
      _currentPositionSeconds = newPosition;
      // Optimistic update of string
      _currentPositionString = TimeSpan.FromSeconds(newPosition).ToString(@"mm\\:ss");
      
      // Call API
      await AudioApi.SeekAsync(TimeSpan.FromSeconds(newPosition));
  }

  // Update timer to drive local progress
  private void StartLocalProgressTimer()
  {
      _localProgressTimer?.Dispose();
      _localProgressTimer = new System.Threading.Timer(_ => 
      {
          if (_isPlaying && _currentPositionSeconds < _totalDurationSeconds)
          {
               _currentPositionSeconds += 1;
               _currentPositionString = TimeSpan.FromSeconds(_currentPositionSeconds).ToString(@"mm\\:ss");
               InvokeAsync(StateHasChanged);
          }
      }, null, 1000, 1000);
  }
"""

# Need to find where to insert. method RefreshPlaybackStateAsync needs update too.
# This approach is getting risky with regex replacement on a potentially modified file.
# Better to just use replace logic for the method RefreshPlaybackStateAsync

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)

print("Updates written")
