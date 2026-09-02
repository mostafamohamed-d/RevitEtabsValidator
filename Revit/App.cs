using Autodesk.Revit.UI;
using System.Reflection;
namespace RevitEtabsValidator;
public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tab="Structural QA"; try{application.CreateRibbonTab(tab);}catch{}
        var panel=application.CreateRibbonPanel(tab,"Model Coordination");
        var asm=Assembly.GetExecutingAssembly().Location;
        var button=new PushButtonData("RevitEtabsValidator","Revit ↔ ETABS\nValidator",asm,"RevitEtabsValidator.Revit.Commands.ShowValidatorCommand");
        var pb=panel.AddItem(button) as PushButton; if(pb!=null){pb.ToolTip="Compare Revit 2025 structural framing/columns against ETABS.";}
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication application)=>Result.Succeeded;
}
