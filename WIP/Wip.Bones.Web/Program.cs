using Wip.Bones.Web;

var builder = WebApplication.CreateBuilder(args);
BonesWebHost.AddBonesViewerServices(builder.Services);

var app = builder.Build();
BonesWebHost.MapMatchRoutes(app);
BonesWebHost.MapStaticViewer(app);

app.Run();

public partial class Program;
