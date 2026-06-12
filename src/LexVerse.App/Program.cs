using LexVerse.Core.ScreenCapture;
using LexVerse.OCR;

Console.WriteLine("LexVerse OCR module");
Console.WriteLine("Pipeline: Windows Graphics Capture -> exact frame change check -> Windows OCR.");
Console.WriteLine("UI layer will create a GraphicsCaptureItem, then call WindowsGraphicsCaptureSession.Create(item).");
Console.WriteLine("When any pixel changes, LexVerse OCRs the full captured frame; no significance threshold is used.");

var changeDetector = new ExactFrameChangeDetector();
var ocrService = new WindowsOcrService();

Console.WriteLine($"Ready: {changeDetector.GetType().Name} + {ocrService.GetType().Name}.");
