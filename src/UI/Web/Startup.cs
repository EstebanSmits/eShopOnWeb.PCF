﻿using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text;
using Microsoft.eShopWeb.Comm;
using Pivotal.Discovery.Client;
using Steeltoe.Common.Discovery;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Steeltoe.Management.CloudFoundry;
using Steeltoe.Management.Endpoint.CloudFoundry;
using Steeltoe.Common.HealthChecks;
using Steeltoe.Management.Endpoint.Info;


namespace Microsoft.eShopWeb.Web
{
    public class Startup
    {
        private IServiceCollection _services;

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // public void ConfigureDevelopmentServices(IServiceCollection services)
        // {
        //     // use in-memory database
        //     ConfigureInMemoryDatabases(services);

        //     // use real database
        //     // ConfigureProductionServices(services);
        // }

        // private void ConfigureInMemoryDatabases(IServiceCollection services)
        // {
        //     // use in-memory database
        //     services.AddDbContext<CatalogContext>(c =>
        //         c.UseInMemoryDatabase("Catalog"));

        //     // Add Identity DbContext
        //     services.AddDbContext<AppIdentityDbContext>(options =>
        //         options.UseInMemoryDatabase("Identity"));

        //     //ConfigureServices(services);
        // }

        // public void ConfigureProductionServices(IServiceCollection services)
        // {
        //     // use real database
        //     // Requires LocalDB which can be installed with SQL Server Express 2016
        //     // https://www.microsoft.com/en-us/download/details.aspx?id=54284
        //     services.AddDbContext<CatalogContext>(c =>
        //         c.UseSqlServer(connectionString: Configuration.GetConnectionString("CatalogConnection")));

        //     // Add Identity DbContext
        //     services.AddDbContext<AppIdentityDbContext>(c =>
        //         c.UseSqlServer(connectionString: Configuration.GetConnectionString("IdentityConnection")));

        //     //ConfigureServices(services);
        // }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<AppIdentityDbContext>()
                .AddDefaultTokenProviders();

            services.ConfigureApplicationCookie(options =>
            {
                options.Cookie.HttpOnly = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                options.LoginPath = "/Account/Signin";
                options.LogoutPath = "/Account/Signout";
                options.Cookie = new CookieBuilder
                {
                    IsEssential = true // required for auth to work without explicit user consent; adjust to suit your privacy policy
                };
            });

            services.AddDbContext<CatalogContext>(c => c.UseInMemoryDatabase("Catalog"));
            services.AddDbContext<AppIdentityDbContext>(c => c.UseInMemoryDatabase("Identity"));

            services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
            services.AddScoped(typeof(IAsyncRepository<>), typeof(EfRepository<>));

            services.AddScoped<ICatalogService, CachedCatalogService>();
            services.AddScoped<IBasketService, BasketService>();
            services.AddScoped<IBasketViewModelService, BasketViewModelService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IOrderingService, OrderingService>();
            services.AddScoped<CatalogService>();
            services.Configure<CatalogSettings>(Configuration);
            services.AddSingleton<IUriComposer>(new UriComposer(Configuration.Get<CatalogSettings>()));

            services.AddScoped(typeof(IAppLogger<>), typeof(LoggerAdapter<>));
            services.AddTransient<IEmailSender, EmailSender>();

            // Add memory cache services
            services.AddMemoryCache();

            services.AddOptions();
            services.Configure<AppSettings>(Configuration);
            
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            //register delegating handlers
            services.AddTransient<HttpClientAuthorizationDelegatingHandler>();
            services.AddTransient<HttpClientRequestIdDelegatingHandler>();

            //set 5 min as the lifetime for each HttpMessageHandler int the pool
            services.AddHttpClient("extendedhandlerlifetime").SetHandlerLifetime(TimeSpan.FromMinutes(5));

            //add http client services

            //services.AddHttpClient<ICatalogService, CatalogService>();

            services.AddMvc();
            
            services.AddCloudFoundryActuators(Configuration);
            services.AddDiscoveryClient(Configuration);

            services.AddSingleton<ICatalogService>(sp =>
            {
                var handler = new DiscoveryHttpClientHandler(sp.GetService<IDiscoveryClient>());
                var httpClient = new HttpClient(handler, false)
                {
                    BaseAddress = new Uri(Configuration.GetValue<string>("CatalogBaseUrl"))
                };
                var logger = sp.GetService<ILogger<CatalogService>>();
                return new CatalogService(httpClient, logger);
            });

            _services = services;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                ListAllRegisteredServices(app);
                app.UseDatabaseErrorPage();
            }
            else
            {
                app.UseExceptionHandler("/Catalog/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseAuthentication();

            app.UseMvc();
            app.UseCloudFoundryActuators();
            app.UseDiscoveryClient();
        }

        private void ListAllRegisteredServices(IApplicationBuilder app)
        {
            app.Map("/allservices", builder => builder.Run(async context =>
            {
                var sb = new StringBuilder();
                sb.Append("<h1>All Services</h1>");
                sb.Append("<table><thead>");
                sb.Append("<tr><th>Type</th><th>Lifetime</th><th>Instance</th></tr>");
                sb.Append("</thead><tbody>");
                foreach (var svc in _services)
                {
                    sb.Append("<tr>");
                    sb.Append($"<td>{svc.ServiceType.FullName}</td>");
                    sb.Append($"<td>{svc.Lifetime}</td>");
                    sb.Append($"<td>{svc.ImplementationType?.FullName}</td>");
                    sb.Append("</tr>");
                }
                sb.Append("</tbody></table>");
                await context.Response.WriteAsync(sb.ToString());
            }));
        }
    }
}
