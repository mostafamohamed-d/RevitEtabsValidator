using RevitEtabsValidator.Core.Geometry;
using RevitEtabsValidator.Core.Models;
namespace RevitEtabsValidator.ETABS;




































































































public sealed class EtabsModelReader
{
    private readonly dynamic _sap;
    public EtabsModelReader(dynamic sap){_sap=sap;}
    public List<ColumnElement> ReadColumns()=>ReadFrames<ColumnElement>(true);
    public List<BeamElement> ReadBeams()=>ReadFrames<BeamElement>(false);

    private List<T> ReadFrames<T>(bool columns) where T:ElementBase,new()
    {
        var list=new List<T>();
        int n=0; string[] names=Array.Empty<string>();
        int rc=_sap.FrameObj.GetNameList(ref n,ref names); if(rc!=0) return list;
        for(int i=0;i<names.Length;i++)
        {
            var name=names[i]; try
            {
                string p1="",p2=""; _sap.FrameObj.GetPoints(name,ref p1,ref p2);
                if(string.IsNullOrWhiteSpace(p1)||string.IsNullOrWhiteSpace(p2)) continue;
                var a=Point(_sap,p1); var b=Point(_sap,p2);
                string label=name, story=""; try{_sap.FrameObj.GetLabelFromName(name,ref label,ref story);}catch{}
                string prop=""; bool sauto=false; try{_sap.FrameObj.GetSection(name,ref prop,ref sauto);}catch{}
                (double w,double d)=Section(prop);
                // ETABS story label is preferred; if unavailable infer by closest story elevation to midpoint.
                if(string.IsNullOrWhiteSpace(story)) story=ClosestStory((a.Z+b.Z)/2);
                if(columns)
                {
                    double angle=0; try{double a2=0,a3=0;_sap.FrameObj.GetLocalAxes(name,ref a2,ref a3);angle=a3;}catch{}
                    var c=new ColumnElement{Id=name,Name=label,SectionName=prop,LevelName=story,Source=SourceApplication.Etabs,StartPoint=a,EndPoint=b,BaseElevation=Math.Min(a.Z,b.Z),TopElevation=Math.Max(a.Z,b.Z),Width=w,Depth=d,Rotation=angle}; list.Add((T)(ElementBase)c);
                }
                else list.Add((T)(ElementBase)new BeamElement{Id=name,Name=label,SectionName=prop,LevelName=story,Source=SourceApplication.Etabs,StartPoint=a,EndPoint=b,Width=w,Depth=d});
            }catch(Exception ex){System.Diagnostics.Debug.WriteLine($"ETABS frame {name}: {ex.Message}");}
        }
        return list;
    }
    private Point3D Point(dynamic sap,string point)
    {double x=0,y=0,z=0; int rc=sap.PointObj.GetCoordCartesian(point,ref x,ref y,ref z,"Global"); return new Point3D(x,y,z);}
    private (double w,double d) Section(string prop)
    {
        if(string.IsNullOrWhiteSpace(prop)) return (0,0);
        try{string mat="";double t3=0,t2=0;int rc=_sap.PropFrame.GetRectangle(prop,ref mat,ref t3,ref t2);if(rc==0)return (t2,t3);}catch{}
        return (0,0);
    }
    private string ClosestStory(double z)
    {try{int n=0;string[] ss=Array.Empty<string>();_sap.Story.GetNameList(ref n,ref ss);string best="";double d=double.MaxValue;foreach(var s in ss){double e=0;try{_sap.Story.GetElevation(s,ref e);}catch{continue;}var dd=Math.Abs(e-z);if(dd<d){d=dd;best=s;}}return best;}catch{return "";}}
}
