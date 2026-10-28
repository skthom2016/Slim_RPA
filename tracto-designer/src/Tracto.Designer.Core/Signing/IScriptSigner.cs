namespace Tracto.Designer.Core.Signing;

using Tracto.Designer.Core.Scripting;

public interface IScriptSigner
{
    string SignerId { get; }
    ScriptSignature Sign(Script script);
    bool Verify(Script script, ScriptSignature signature);
}
