namespace TravelCut.Domain.Enums;

public enum MediaKind
{
    Video,
    Audio,
    Image,
    Other
}

public enum SceneClassification
{
    Conversation,
    Landscape,
    Travel,
    Drone,
    StoreProduct,
    Broll,
    Other
}

public enum SuggestionKind
{
    Silence,
    Hesitation,
    FalseStart,
    Repetition,
    SpeedBroll,
    SubtleZoom,
    MusicBed,
    Transition
}

public enum SuggestionStatus
{
    Proposed,
    Approved,
    Rejected,
    Deferred,
    Modified
}

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum JobType
{
    Inventory,
    AudioExtraction,
    Transcription,
    VadSilence,
    SceneDetection,
    EditorialPlanning,
    DaVinciExport
}
