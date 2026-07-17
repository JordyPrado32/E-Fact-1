namespace Simetric.Services
{
    public class MenuStateService
    {
        public event Action? OnMenuChanged;

        public void NotifyMenuChanged()
        {
            OnMenuChanged?.Invoke();
        }
    }
}