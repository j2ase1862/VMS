# HTML -> PDF (Chrome headless) + 검수용 페이지 PNG
#   powershell -File docs/gs/pipeline/html2pdf_chrome.ps1 -Html <입력.html> -Pdf <출력.pdf> [-OutDir <PNG 폴더>] [-Pages 1,2] [-Dpi 110]
#
# 별책(AI 학습 도구 매뉴얼)처럼 docx 파이프라인을 타지 않는 HTML 문서를 종이 형식으로 낼 때 쓴다.
# Word COM(docx2pdf_render.ps1)과 달리 브라우저가 CSS 를 그대로 해석하므로 화면과 같은 판면이 나온다.
# 문서 쪽 @media print 에서 흰 바탕·검은 글씨로 바꿔 두어야 한다 — 브라우저는 배경색을 인쇄하지 않는다.
param(
  [Parameter(Mandatory = $true)][string]$Html,
  [Parameter(Mandatory = $true)][string]$Pdf,
  [string]$OutDir,
  [int[]]$Pages = @(),
  [int]$Dpi = 110
)
$ErrorActionPreference = 'Stop'

$chrome = @(
  "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
  "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
  "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) { throw "chrome.exe 를 찾지 못했다" }

$src = (Resolve-Path $Html).Path
$uri = ([System.Uri]("file:///" + $src.Replace('\', '/'))).AbsoluteUri
$prof = Join-Path $env:TEMP ("chrome_pdf_" + [guid]::NewGuid().ToString('N').Substring(0, 8))

# Chrome 은 USB/GCM 잡음을 stderr 로 뱉는데, PowerShell 5.1 은 네이티브 stderr 를 오류 레코드로
# 감싸 버린다(NativeCommandError) — 그래서 호출 연산자(&) 대신 Start-Process 로 띄운다.
if (Test-Path $Pdf) { Remove-Item -Force $Pdf }
$args = @("--headless=new", "--disable-gpu", "--no-first-run", "--user-data-dir=$prof",
          "--no-pdf-header-footer", "--print-to-pdf=$Pdf", $uri)
$proc = Start-Process -FilePath $chrome -ArgumentList $args -PassThru -Wait -WindowStyle Hidden
Remove-Item -Recurse -Force $prof -ErrorAction SilentlyContinue
if (-not (Test-Path $Pdf)) { throw "PDF 가 만들어지지 않았다: $Pdf" }
"PDF written: $Pdf ({0:N0} bytes)" -f (Get-Item $Pdf).Length

if (-not $OutDir) { return }

# 검수용: 지정한 쪽을 PNG 로 (WinRT PDF 렌더러 — docx2pdf_render.ps1 과 같은 방식)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.RandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
function Await($task, $type) {
  $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
  $t = $asTask.MakeGenericMethod($type).Invoke($null, @($task)); $t.Wait(); $t.Result
}
function AwaitAction($task) {
  $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
  $t = $asTask.Invoke($null, @($task)); $t.Wait()
}
New-Item -ItemType Directory -Force $OutDir | Out-Null
$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync((Resolve-Path $Pdf).Path)) ([Windows.Storage.StorageFile])
$doc = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
$total = $doc.PageCount
if ($Pages.Count -eq 0) { $Pages = 1..$total }
foreach ($p in $Pages) {
  if ($p -lt 1 -or $p -gt $total) { continue }
  $page = $doc.GetPage($p - 1)
  $opt = New-Object Windows.Data.Pdf.PdfPageRenderOptions
  $opt.DestinationWidth = [uint32]([math]::Round(8.27 * $Dpi))
  $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
  AwaitAction ($page.RenderToStreamAsync($stream, $opt))
  $out = Join-Path $OutDir ("p{0:D3}.png" -f $p)
  $fs = [System.IO.File]::Create($out)
  $reader = New-Object Windows.Storage.Streams.DataReader($stream.GetInputStreamAt(0))
  $null = Await ($reader.LoadAsync([uint32]$stream.Size)) ([uint32])
  $bytes = New-Object byte[] $stream.Size
  $reader.ReadBytes($bytes)
  $fs.Write($bytes, 0, $bytes.Length); $fs.Close()
  $page.Dispose()
}
"rendered $($Pages.Count) pages of $total to $OutDir"
