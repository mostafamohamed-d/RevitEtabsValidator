using Autodesk.Revit.UI;
using RevitEtabsValidator.ETABS;
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Revit.Services;
using RevitEtabsValidator.Revit.UI;
namespace RevitEtabsValidator.Revit.Commands;
public sealed class RevitRequestHandler : IExternalEventHandler
{
    public RevitRequest Request {get;set;}=RevitRequest.None;
    public string IdToSelect {get;set;}="";
    public MainWindow? Window {get;set;}
    public void Execute(UIApplication app)
    {
        try
        {
            switch(Request)
            {
                case RevitRequest.ReadModels:
                    if(Window==null)return;
                    var doc=app.ActiveUIDocument?.Document; if(doc==null){Window.SetStatus("No active Revit document.");return;}
                    var reader=new RevitElementReader(doc); var rc=reader.ReadColumns(); var rb=reader.ReadBeams();
                    Window.SetRevitElements(rc,rb); Window.SetStatus($"Revit read complete: {rc.Count} columns, {rb.Count} beams.");
                    break;
                case RevitRequest.SelectRevitElement:
                    if(Window!=null && !string.IsNullOrWhiteSpace(IdToSelect) && app.ActiveUIDocument!=null) SelectionService.Select(app.ActiveUIDocument,IdToSelect);
                    break;
            }
        }
        catch(Exception ex){Window?.SetStatus("Revit operation failed: "+ex.Message);}
        finally{Request=RevitRequest.None;}
    }
    public string GetName()=>"Revit ↔ ETABS Validator External Event";
}
