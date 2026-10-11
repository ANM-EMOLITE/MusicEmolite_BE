using CloudinaryDotNet;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MS_API.Realtime;
using MS_Application.DataTransferObjects.Cloudinary;
using MS_Application.DataTransferObjects.Email;
using MS_Application.DataTransferObjects.Lyrics;
using MS_Application.External;
using MS_Application.Helpers;
using MS_Application.Services;
using MS_Application.Services.Interfaces;
using MS_Application.Services.Interfaces.External;
using MS_Application.Services.SongSync;
using MS_Infrastructure.DataAccess;
using MS_Infrastructure.DataAccess.DISTS.Contexts;
using System.Text;

namespace MS_API.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<CrmDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("CRMConnection")));
            services.AddDbContext<DistDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DISTConnection")));

            return services;
        }

        public static IServiceCollection AddSwaggerConfiguration(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "MS API", Version = "v1" });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter 'Bearer' followed by your token."
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                        },
                        Array.Empty<string>()
                    }
                });
            });

            return services;
        }

        public static IServiceCollection AddCorsConfiguration(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod());
            });

            return services;
        }

        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            var key = Encoding.UTF8.GetBytes(configuration["JwtSettings:Secret"]);

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(key)
                    };

                    // SignalR's WebSocket/SSE transports can't set an Authorization
                    // header, so the JS client sends the token as a query string
                    // instead - pull it from there for hub requests only.
                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            var accessToken = context.Request.Query["access_token"];
                            var path = context.HttpContext.Request.Path;

                            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            {
                                context.Token = accessToken;
                            }

                            return Task.CompletedTask;
                        }
                    };
                });

            services.AddAuthorization();
            return services;
        }

        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IOtpService, OtpService>();
            services.AddScoped<JwtHelper>();
            services.AddSingleton<SongSyncState>();
            return services;
        }

        public static IServiceCollection AddRealtimeConfiguration(this IServiceCollection services)
        {
            services.AddSignalR();
            services.AddSingleton<IUserIdProvider, ChatHubUserIdProvider>();
            services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

            return services;
        }

        public static IServiceCollection AddEmailConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<EmailSettingsDto>(
                configuration.GetSection("EmailSettings"));

            services.AddScoped<IEmailService, EmailService>();

            return services;
        }

        public static IServiceCollection AddLyricsApiConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<LyricsApiSettingsDto>(
                configuration.GetSection("LyricsAPI"));

            services.AddHttpClient();

            return services;
        }

        public static IServiceCollection AddCloudinaryConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<CloudinarySettingsDto>(
                "AudioCloudinary",
                configuration.GetSection("CloudinarySettings"));

            services.Configure<CloudinarySettingsDto>(
                "ImageCloudinary",
                configuration.GetSection("CloudinarySettingsImg"));

            services.AddSingleton<Cloudinary>(provider =>
            {
                var settings = provider
                    .GetRequiredService<IOptionsMonitor<CloudinarySettingsDto>>()
                    .Get("AudioCloudinary");

                var account = new Account(
                    settings.CloudName,
                    settings.ApiKey,
                    settings.ApiSecret
                );

                var cloudinary = new Cloudinary(account);
                cloudinary.Api.Secure = true;

                return cloudinary;
            });

            services.AddSingleton<CloudinaryImageService>(provider =>
            {
                var settings = provider
                    .GetRequiredService<IOptionsMonitor<CloudinarySettingsDto>>()
                    .Get("ImageCloudinary");

                var account = new Account(
                    settings.CloudName,
                    settings.ApiKey,
                    settings.ApiSecret
                );

                var cloudinary = new Cloudinary(account);
                cloudinary.Api.Secure = true;

                return new CloudinaryImageService(cloudinary);
            });

            return services;
        }
    }
}