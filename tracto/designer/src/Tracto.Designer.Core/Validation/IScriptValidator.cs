namespace Tracto.Designer.Core.Validation;

using Tracto.Designer.Core.Scripting;

public interface IScriptValidator
{
    ScriptValidationResult Validate(Script script);
}
