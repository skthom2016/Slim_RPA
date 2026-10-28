namespace Tracto.Designer.Core.Scripting;

public interface IScriptSerializer
{
    string Serialize(Script script);
    Script Deserialize(string content);
}
