namespace RimMind.Application.Common.Interfaces.Abstractions
{
    public interface ITranslationService
    {
        string Translate(string key);
        string Translate(string key, object arg0);
        string Translate(string key, object arg0, object arg1);
        string Translate(string key, object arg0, object arg1, object arg2);
    }
}
