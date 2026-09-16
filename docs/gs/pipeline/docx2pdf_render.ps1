param([string]$Docx, [string]$Pdf, [string]$OutDir, [int[]]$Pages = @(), [int]$Dpi = 60)
$ErrorActionPreference = 'Stop'
# 1) docx -> pdf (Word COM, TOC 갱신 포함, docx 는 저장하지 않음)
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$doc = $word.Documents.Open($Docx, $false, $true)
try { $doc.Fields.Update() | Out-Null } catch {}
try { foreach ($t in $doc.TablesOfContents) { $t.Update() } } catch {}
$doc.ExportAsFixedFormat($Pdf, 17)
$pageCount = $doc.ComputeStatistics(2)
$doc.Close(0)
$word.Quit()
"PDF written: $Pdf pages=$pageCount"
# 2) pdf pages -> png (WinRT)
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
$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])
$pdfDoc = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
$total = $pdfDoc.PageCount
if ($Pages.Count -eq 0) { $Pages = 1..$total }
foreach ($p in $Pages) {
  if ($p -lt 1 -or $p -gt $total) { continue }
  $page = $pdfDoc.GetPage($p - 1)
  $opt = New-Object Windows.Data.Pdf.PdfPageRenderOptions
  $opt.DestinationWidth = [uint32]([math]::Round(8.27 * $Dpi))
  $stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
  AwaitAction ($page.RenderToStreamAsync($stream, $opt))
  $out = Join-Path $OutDir ("p{0:D3}.png" -f $p)
  $fs = [System.IO.File]::Create($out)
  $net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream.GetInputStreamAt(0))
  $net.CopyTo($fs); $fs.Close(); $net.Close(); $stream.Dispose(); $page.Dispose()
}
"rendered $($Pages.Count) pages of $total to $OutDir"
