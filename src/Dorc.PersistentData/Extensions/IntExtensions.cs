namespace Dorc.PersistentData.Extensions
{
    public static class IntExtensions
    {
        public static bool HasAccessLevel(this int Allow, AccessLevel level)
        {
            return (Allow & (int)level) != 0;
        }
    }
}
