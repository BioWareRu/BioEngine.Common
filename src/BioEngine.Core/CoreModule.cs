using System;
using System.Collections.Generic;
using System.Reflection;
using BioEngine.Core.DB;
using BioEngine.Core.Entities;
using BioEngine.Core.Modules;
using BioEngine.Core.Properties;
using BioEngine.Core.Repository;
using BioEngine.Core.Search;
using BioEngine.Core.Search.ElasticSearch;
using BioEngine.Core.Seo;
using BioEngine.Core.Storage;
using BioEngine.Core.Web;
using FluentValidation;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BioEngine.Core
{
    public class CoreModule : BioEngineModule<CoreModuleConfig>
    {
        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration,
            IHostingEnvironment environment)
        {
            if (Config.EnableInMemoryDatabase)
            {
                AddInMemoryDatabase(services);
            }
            else if (Config.EnableDatabase)
            {
                AddPostgresDatabase(services, configuration, environment);
            }

            if (Config.EnableValidation)
            {
                AddValidation(services);
            }

            if (Config.EnableSeoExtensions)
            {
                AddSeo();
            }

            if (Config.EnableFileStorage)
            {
                AddFileStorage(services, configuration);
            }

            if (Config.EnableS3Storage)
            {
                AddS3Storage(services, configuration);
            }

            if (Config.EnableElasticSearch)
            {
                AddElasticSearch(services, configuration);
            }

            services.AddScoped<BaseControllerContext>();
            services.AddScoped(typeof(BaseControllerContext<>));
        }

        private void AddSearch(IServiceCollection services)
        {
            var assembliesList = new List<Assembly>(Config.Assemblies) {typeof(ISearchProvider).Assembly};
            services.Scan(s =>
                s.FromAssemblies(assembliesList).AddClasses(classes => classes.AssignableTo<ISearchProvider>())
                    .AsSelfWithInterfaces().WithScopedLifetime());
            services.AddScoped<IRepositoryHook, SearchRepositoryHook<Page>>();
            services.AddScoped<IRepositoryHook, SearchRepositoryHook<Post>>();
            foreach (var assembly in assembliesList)
            {
                foreach (var definedType in assembly.DefinedTypes)
                {
                    if (definedType.IsAbstract || definedType.BaseType == null)
                    {
                        continue;
                    }

                    if (!typeof(Section).IsAssignableFrom(definedType))
                    {
                        continue;
                    }

                    var type = typeof(SearchRepositoryHook<>).MakeGenericType(definedType);
                    services.AddScoped(typeof(IRepositoryHook), type);
                }
            }
        }

        private void AddElasticSearch(IServiceCollection services, IConfiguration configuration)
        {
            AddSearch(services);
            services.AddScoped<ISearcher, ElasticSearcher>();
            services.Configure<ElasticSearcherOptions>(options =>
            {
                var uri = configuration["BE_ELASTICSEARCH_URI"];
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ArgumentException("Elastic url is empty");
                }

                options.Url = uri;
                options.Login = configuration["BE_ELASTICSEARCH_LOGIN"];
                options.Password = configuration["BE_ELASTICSEARCH_PASSWORD"];
            });
        }

        private static void AddS3Storage(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<S3StorageOptions>(o =>
            {
                var uri = configuration["BE_STORAGE_PUBLIC_URI"];
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ArgumentException("Storage url is empty");
                }

                var success = Uri.TryCreate(uri, UriKind.Absolute, out var publicUri);
                if (!success)
                {
                    throw new ArgumentException($"URI {uri} is not proper URI");
                }

                var serverUriStr = configuration["BE_STORAGE_S3_SERVER_URI"];
                if (string.IsNullOrEmpty(serverUriStr))
                {
                    throw new ArgumentException("S3 server url is empty");
                }

                var bucketName = configuration["BE_STORAGE_S3_BUCKET"];
                if (string.IsNullOrEmpty(bucketName))
                {
                    throw new ArgumentException("S3 bucketName is empty");
                }

                var accessKey = configuration["BE_STORAGE_S3_ACCESS_KEY"];
                if (string.IsNullOrEmpty(accessKey))
                {
                    throw new ArgumentException("S3 access key is empty");
                }

                var secretKey = configuration["BE_STORAGE_S3_SECRET_KEY"];
                if (string.IsNullOrEmpty(secretKey))
                {
                    throw new ArgumentException("S3 secret key is empty");
                }

                success = Uri.TryCreate(serverUriStr, UriKind.Absolute, out var serverUri);
                if (!success)
                {
                    throw new ArgumentException($"S3 server URI {uri} is not proper URI");
                }

                o.PublicUri = publicUri;
                o.Server = serverUri;
                o.Bucket = bucketName;
                o.AccessKey = accessKey;
                o.SecretKey = secretKey;
            });
            services.AddScoped<IStorage, S3Storage>();
        }

        private static void AddFileStorage(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<FileStorageOptions>(o =>
            {
                var path = configuration["BE_STORAGE_FILE_PATH"];
                if (string.IsNullOrEmpty(path))
                {
                    throw new ArgumentException("File storage path is empty");
                }

                var uri = configuration["BE_STORAGE_PUBLIC_URI"];
                if (string.IsNullOrEmpty(uri))
                {
                    throw new ArgumentException("Storage url is empty");
                }

                var success = Uri.TryCreate(uri, UriKind.Absolute, out var publicUri);
                if (!success)
                {
                    throw new ArgumentException($"URI {uri} is not proper URI");
                }

                o.PublicUri = publicUri;
                o.StoragePath = path;
            });
            services.AddScoped<IStorage, FileStorage>();
        }

        private static void AddSeo()
        {
            PropertiesProvider.RegisterBioEngineSectionProperties<SeoPropertiesSet>("seo");
            PropertiesProvider.RegisterBioEngineContentProperties<SeoPropertiesSet>("seo");
            PropertiesProvider.RegisterBioEngineProperties<SeoPropertiesSet, Site>("seo");
            PropertiesProvider.RegisterBioEngineProperties<SeoPropertiesSet, Page>("seo");
        }

        private void AddValidation(IServiceCollection services)
        {
            var assembliesList = new List<Assembly>(Config.Assemblies) {typeof(BioContext).Assembly};
            foreach (var assembly in assembliesList)
            {
                var validators = AssemblyScanner.FindValidatorsInAssembly(assembly);
                foreach (var validator in validators)
                {
                    services.AddScoped(validator.InterfaceType, validator.ValidatorType);
                }
            }
        }

        private void AddRepositories(IServiceCollection services)
        {
            services.AddScoped<PropertiesProvider>();

            // collect defined types
            var assembliesList = new List<Assembly>(Config.Assemblies) {typeof(BioContext).Assembly};
            if (Config.MigrationsAssembly != null)
            {
                assembliesList.Add(Config.MigrationsAssembly);
            }

            var types = new HashSet<TypeInfo>();
            foreach (var assembly in assembliesList)
            {
                foreach (var definedType in assembly.DefinedTypes)
                {
                    types.Add(definedType);
                }
            }

            services.AddScoped<BioRepositoryHooksManager>();
            services.Scan(s =>
                s.FromAssemblies(assembliesList).AddClasses(classes => classes.AssignableTo<IBioRepository>())
                    .AsSelfWithInterfaces());
            var metadataManager = new BioEntityMetadataManager();
            foreach (var type in types)
            {
                metadataManager.Register(type);
            }

            services.AddSingleton(metadataManager);
            services.AddScoped(typeof(BioRepositoryContext<>));
        }


        private void AddInMemoryDatabase(IServiceCollection services)
        {
            services.AddEntityFrameworkInMemoryDatabase();
            services.AddDbContext<BioContext>((p, options) =>
            {
                options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .UseInMemoryDatabase(Config.InMemoryDatabaseName).UseInternalServiceProvider(p);
            });


            AddRepositories(services);
        }

        private void AddPostgresDatabase(IServiceCollection services, IConfiguration configuration,
            IHostingEnvironment environment)
        {
            var connBuilder = new NpgsqlConnectionStringBuilder
            {
                Host = configuration["BE_POSTGRES_HOST"] ?? "localhost",
                Port = !string.IsNullOrEmpty(configuration["BE_POSTGRES_PORT"])
                    ? int.Parse(configuration["BE_POSTGRES_PORT"])
                    : 5432,
                Username = configuration["BE_POSTGRES_USERNAME"] ?? "postgres",
                Password = configuration["BE_POSTGRES_PASSWORD"] ?? "",
                Database = configuration["BE_POSTGRES_DATABASE"] ?? "brc",
                Pooling = false
            };

            Config.DbConfigure?.Invoke(connBuilder, configuration);
            services.AddEntityFrameworkNpgsql();
            services.AddDbContextPool<BioContext>((p, options) =>
            {
                options.UseNpgsql(connBuilder.ConnectionString,
                    builder => builder.MigrationsAssembly(Config.MigrationsAssembly != null
                        ? Config.MigrationsAssembly.FullName
                        : typeof(DbContext).Assembly.FullName)).UseInternalServiceProvider(p);
                if (environment.IsDevelopment())
                {
                    options.EnableSensitiveDataLogging();
                }
            });

            AddRepositories(services);
        }
    }

    [PublicAPI]
    public class CoreModuleConfig
    {
        public bool EnableDatabase = true;
        public bool EnableInMemoryDatabase;
        public string InMemoryDatabaseName;
        public bool EnableValidation;
        public bool EnableFileStorage;
        public bool EnableS3Storage = true;
        public bool EnableElasticSearch;
        public bool EnableSeoExtensions = true;
        public Action<NpgsqlConnectionStringBuilder, IConfiguration> DbConfigure;
        public List<Assembly> Assemblies { get; } = new List<Assembly>();
        public Assembly MigrationsAssembly;
    }
}
