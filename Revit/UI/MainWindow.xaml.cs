using Autodesk.Revit.UI;
using RevitEtabsValidator.Core.Comparison;
using RevitEtabsValidator.Core.Models;
using RevitEtabsValidator.Core.Validation;
using RevitEtabsValidator.ETABS;
using RevitEtabsValidator.Revit.Commands;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace RevitEtabsValidator.Revit.UI;
public partial class MainWindow:Window
{
    private readonly UIApplication _uiapp; private readonly RevitRequestHandler _handler; private readonly ExternalEvent _event;
    private readonly EtabsConnection _etabs=new(); private List<ColumnElement> _revitColumns=new(),_etabsColumns=new(); private List<BeamElement> _revitBeams=new(),_etabsBeams=new();
    private ValidationReport _columnReport=new(),_beamReport=new(); private List<ValidationResult> _all=new(); private readonly ObservableCollection<ValidationResult> _visible=new(); private ValidationResult? _selected;
    public MainWindow(UIApplication uiapp){InitializeComponent();_uiapp=uiapp;_handler=new RevitRequestHandler{Window=this};_event=ExternalEvent.Create(_handler);ResultsGrid.ItemsSource=_visible;}
    public void SetRevitElements(List<ColumnElement> c,List<BeamElement>b){_revitColumns=c;_revitBeams=b;RevitColCount.Text=c.Count.ToString();RevitBeamCount.Text=b.Count.ToString();}
    public void SetStatus(string s)=>StatusText.Text=s;
    private void Raise(RevitRequest r,string id=""){_handler.Request=r;_handler.IdToSelect=id;_event.Raise();}
    private void ReadRevit_Click(object s,RoutedEventArgs e)=>Raise(RevitRequest.ReadModels);
    private void ConnectEtabs_Click(object s,RoutedEventArgs e){var ok=_etabs.ConnectRunning();if(!ok && StartEtabs.IsChecked==true)ok=_etabs.StartAndConnect();if(ok){SetStatus(_etabs.Message);ReadEtabs();}else SetStatus(_etabs.Message+" Tick \"Start ETABS if not running\" to let the tool start ETABS.");}
    private void ReadEtabs(){try{_etabs.SetUnitsKnMmC();var r=new EtabsModelReader(_etabs.SapModel);_etabsColumns=r.ReadColumns();_etabsBeams=r.ReadBeams();EtabsColCount.Text=_etabsColumns.Count.ToString();EtabsBeamCount.Text=_etabsBeams.Count.ToString();SetStatus($"ETABS read complete: {_etabsColumns.Count} columns, {_etabsBeams.Count} beams.");}catch(Exception ex){SetStatus("ETABS read failed: "+ex.Message);}}
    private ValidationTolerance Tol()=>new(){PositionToleranceMm=Read(PositionTol,25),ElevationToleranceMm=Read(ElevationTol,25),DimensionToleranceMm=Read(SectionTol,5),LengthToleranceMm=Read(LengthTol,25),AngleToleranceDegrees=Read(AngleTol,1)};
    private static double Read(TextBox b,double d)=>double.TryParse(b.Text,out var v)&&v>=0?v:d;
    private void RunValidation_Click(object s,RoutedEventArgs e){try{if(!_etabs.IsConnected){SetStatus("Connect ETABS first.");return;}if(_revitColumns.Count==0&&_revitBeams.Count==0)Raise(RevitRequest.ReadModels);ReadEtabs();var t=Tol();var cmp=new ModelComparer();_columnReport=cmp.CompareColumns(_revitColumns,_etabsColumns,t);_beamReport=cmp.CompareBeams(_revitBeams,_etabsBeams,t);_all=_columnReport.Results.Concat(_beamReport.Results).OrderBy(x=>x.StoryOrLevel).ThenBy(x=>x.ElementType).ThenBy(x=>x.RevitName??x.EtabsName).ToList();RefreshVisible();UpdateSummary();PopulateFloors();SetStatus($"Validation complete: {_all.Count} checks.");}catch(Exception ex){SetStatus("Validation failed: "+ex);}}
    private void UpdateSummary(){MatchedCount.Text=_all.Count(x=>x.Status==ValidationStatus.Matched).ToString();WarningCount.Text=_all.Count(x=>x.Severity==Severity.Warning).ToString();ErrorCount.Text=_all.Count(x=>x.Severity==Severity.Error||x.Severity==Severity.Critical).ToString();MissingCount.Text=_all.Count(x=>x.Status==ValidationStatus.MissingInRevit||x.Status==ValidationStatus.MissingInEtabs).ToString();}
    private void RefreshVisible(){_visible.Clear();var f=FilterBox.SelectedItem as ComboBoxItem;var floor=FloorBox.SelectedItem as string;foreach(var x in _all){var fc=f?.Content?.ToString()??"All";bool ok=fc=="All"||(fc=="Matched"&&x.Status==ValidationStatus.Matched)||(fc=="Warnings"&&x.Severity==Severity.Warning)||(fc=="Errors"&&(x.Severity==Severity.Error||x.Severity==Severity.Critical))||(fc=="Missing"&&(x.Status==ValidationStatus.MissingInRevit||x.Status==ValidationStatus.MissingInEtabs));if(ok&&!string.IsNullOrWhiteSpace(floor)&&floor!="All"&&!string.Equals(x.StoryOrLevel,floor,StringComparison.OrdinalIgnoreCase))ok=false;if(ok)_visible.Add(x);}}
    private void FilterBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(IsInitialized)RefreshVisible();}
    private void FloorBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(IsInitialized)RefreshVisible();}
    private void PopulateFloors(){var floors=_all.Select(x=>x.StoryOrLevel).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();FloorBox.Items.Clear();FloorBox.Items.Add("All");foreach(var f in floors)FloorBox.Items.Add(f);FloorBox.SelectedIndex=0;PlanFloorBox.ItemsSource=floors;PlanFloorBox.SelectedIndex=floors.Count>0?0:-1;}
    private void ResultsGrid_SelectionChanged(object s,SelectionChangedEventArgs e){_selected=ResultsGrid.SelectedItem as ValidationResult;ShowDetails();if(_selected!=null&&PlanFloorBox.Items.Contains(_selected.StoryOrLevel))PlanFloorBox.SelectedItem=_selected.StoryOrLevel;}
    private void ShowDetails(){if(_selected==null){DetailsText.Text="No result selected.";return;}var sb=new StringBuilder();sb.AppendLine($"{_selected.ElementType} / {_selected.Status} / {_selected.Severity}");sb.AppendLine($"Level: {_selected.StoryOrLevel}");sb.AppendLine($"Revit: {_selected.RevitName} [{_selected.RevitElementId}]");sb.AppendLine($"ETABS: {_selected.EtabsName} [{_selected.EtabsElementId}]");sb.AppendLine();sb.AppendLine(_selected.Message);foreach(var d in _selected.Differences)sb.AppendLine($"{d.Key}: {d.Value}");DetailsText.Text=sb.ToString();}
    private void SelectInRevit_Click(object s,RoutedEventArgs e){if(_selected?.RevitElementId!=null)Raise(RevitRequest.SelectRevitElement,_selected.RevitElementId);}
    private void PlanFloorBox_SelectionChanged(object s,SelectionChangedEventArgs e){if(IsInitialized)DrawPlan(PlanFloorBox.SelectedItem?.ToString()??"");}
    private void DrawPlan(string level)
    {
        PlanCanvas.Children.Clear();if(string.IsNullOrWhiteSpace(level))return;
        var revB=_revitBeams.Where(x=>string.Equals(x.LevelName,level,StringComparison.OrdinalIgnoreCase)).ToList();var etaB=_etabsBeams.Where(x=>string.Equals(x.LevelName,level,StringComparison.OrdinalIgnoreCase)).ToList();
        var revC=_revitColumns.Where(x=>string.Equals(x.LevelName,level,StringComparison.OrdinalIgnoreCase)).ToList();var etaC=_etabsColumns.Where(x=>string.Equals(x.LevelName,level,StringComparison.OrdinalIgnoreCase)).ToList();
        var pts=new List<Point3D>();pts.AddRange(revB.SelectMany(x=>new[]{x.StartPoint,x.EndPoint}));pts.AddRange(etaB.SelectMany(x=>new[]{x.StartPoint,x.EndPoint}));pts.AddRange(revC.Select(x=>x.CenterPoint));pts.AddRange(etaC.Select(x=>x.CenterPoint));if(pts.Count==0){return;}
        double minX=pts.Min(p=>p.X),maxX=pts.Max(p=>p.X),minY=pts.Min(p=>p.Y),maxY=pts.Max(p=>p.Y);double w=Math.Max(1,maxX-minX),h=Math.Max(1,maxY-minY);double cw=Math.Max(100,PlanCanvas.ActualWidth-20),ch=Math.Max(100,PlanCanvas.ActualHeight-20);if(double.IsNaN(cw)||double.IsInfinity(cw))cw=700;if(double.IsNaN(ch)||double.IsInfinity(ch))ch=500;double scale=Math.Min(cw/w,ch/h)*0.92;Point P(Point3D p)=>new((p.X-minX)*scale+10,(maxY-p.Y)*scale+10);
        foreach(var b in revB)Line(P(b.StartPoint),P(b.EndPoint),Brushes.Gray,2,false);foreach(var b in etaB)Line(P(b.StartPoint),P(b.EndPoint),Brushes.DarkGray,2,true);
        foreach(var c in revC)Circle(P(c.CenterPoint),5,Brushes.SteelBlue);foreach(var c in etaC)Circle(P(c.CenterPoint),4,Brushes.Orange, true);
        foreach(var r in _all.Where(x=>string.Equals(x.StoryOrLevel,level,StringComparison.OrdinalIgnoreCase)&&x.Status!=ValidationStatus.Matched))
        {var src=FindPoint(r);if(src.HasValue){var q=P(src.Value);Circle(q,7,Brushes.Red,true);}}
        void Line(Point a,Point b,Brush brush,double thickness,bool dash){var l=new Line{X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y,Stroke=brush,StrokeThickness=thickness};if(dash)l.StrokeDashArray=new System.Windows.Media.DoubleCollection{5,4};PlanCanvas.Children.Add(l);} void Circle(Point q,double rad,Brush brush,bool hollow=false){var el=new Ellipse{Width=rad*2,Height=rad*2,Stroke=brush,StrokeThickness=2,Fill=hollow?Brushes.Transparent:brush};Canvas.SetLeft(el,q.X-rad);Canvas.SetTop(el,q.Y-rad);PlanCanvas.Children.Add(el);}
    }
    private Point3D? FindPoint(ValidationResult r){if(r.RevitElementId!=null){var c=_revitColumns.FirstOrDefault(x=>x.Id==r.RevitElementId);if(c!=null)return c.CenterPoint;var b=_revitBeams.FirstOrDefault(x=>x.Id==r.RevitElementId);if(b!=null)return b.CenterPoint;}if(r.EtabsElementId!=null){var c=_etabsColumns.FirstOrDefault(x=>x.Id==r.EtabsElementId);if(c!=null)return c.CenterPoint;var b=_etabsBeams.FirstOrDefault(x=>x.Id==r.EtabsElementId);if(b!=null)return b.CenterPoint;}return null;}
    private void ExportCsv_Click(object s,RoutedEventArgs e){if(_all.Count==0){SetStatus("Run validation first.");return;}var dlg=new Microsoft.Win32.SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName="Revit_ETABS_Validation.csv"};if(dlg.ShowDialog()!=true)return;var sb=new StringBuilder();sb.AppendLine("Type,Level,Revit,ETABS,Status,Severity,Pos_mm,Elev_mm,Width_mm,Depth_mm,Length_mm,Rotation_deg,Confidence,Message");foreach(var x in _all)sb.AppendLine(string.Join(",",new[]{x.ElementType,x.StoryOrLevel,x.RevitName,x.EtabsName,x.Status.ToString(),x.Severity.ToString(),x.PositionDeltaMm.ToString("F1"),x.ElevationDeltaMm.ToString("F1"),x.WidthDeltaMm.ToString("F1"),x.DepthDeltaMm.ToString("F1"),x.LengthDeltaMm.ToString("F1"),x.RotationDeltaDeg.ToString("F1"),x.Confidence.ToString("F0"),Escape(x.Message)}));File.WriteAllText(dlg.FileName,sb.ToString(),Encoding.UTF8);SetStatus("CSV exported: "+dlg.FileName);}
    private static string Escape(string? s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";
    private void ExportJson_Click(object s,RoutedEventArgs e){if(_all.Count==0){SetStatus("Run validation first.");return;}var dlg=new Microsoft.Win32.SaveFileDialog{Filter="JSON (*.json)|*.json",FileName="Revit_ETABS_Validation.json"};if(dlg.ShowDialog()!=true)return;File.WriteAllText(dlg.FileName,JsonSerializer.Serialize(_all,new JsonSerializerOptions{WriteIndented=true}));SetStatus("JSON exported: "+dlg.FileName);}
}
