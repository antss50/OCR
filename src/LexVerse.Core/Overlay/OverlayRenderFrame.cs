using LexVerse.Core.Pipeline;

namespace LexVerse.Core.Overlay;

public sealed record OverlayRenderFrame(
    IReadOnlyList<OverlayTextItem> Items,
    PipelineTrace? Trace,
    long GeometryVersion,
    DateTimeOffset Timestamp);
