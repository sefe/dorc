namespace Dorc.PersistentData.Sources.Interfaces;

public interface ISharedResourceMutationSource
{
    T ExecuteMutation<T>(Func<T> mutation);
}
