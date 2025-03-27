var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.BRU_AVTOPARK_AspireAPI_ApiService>("apiservice");


builder.Build().Run();
