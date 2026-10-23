namespace Dorc.PersistentData.Sources
{
    /// <summary>
    /// Outcome of attaching/detaching an environment component (container, cloud
    /// resource, API registration), so callers can map to HTTP results without
    /// exception-driven flow. The behavioural exists-check makes duplicate attaches
    /// a handled outcome; the composite PK on the join table is the DB backstop.
    /// </summary>
    public enum EnvironmentAttachmentOutcome
    {
        Attached,
        AlreadyAttached,
        Detached,
        NotAttached,
        ItemNotFound,
        EnvironmentNotFound
    }
}
