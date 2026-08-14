namespace ProductAdvisor.Domain;

/// <summary>
/// The policy area a <see cref="StoreDocument"/> covers (spec.md 002 FR-015, data-model.md).
/// Extensible by adding a member — an addition never invalidates already-stored documents, and
/// retrieval treats an unrecognized-for-this-question type as "no type preference" rather than a
/// filter mismatch (FR-022).
/// </summary>
public enum DocumentType
{
    Delivery,
    Payment,
    Returns,
    Warranty,
    Loyalty,
    Contacts,
    Other,
}
