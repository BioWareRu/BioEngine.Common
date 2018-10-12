﻿using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BioEngine.Core.Entities;
using BioEngine.Core.Interfaces;
using BioEngine.Core.Storage;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Newtonsoft.Json;

namespace BioEngine.Core.DB
{
    public sealed class BioContext : DbContext
    {
        public static TypesProvider TypesProvider;

        // ReSharper disable once SuggestBaseTypeForParameter
        public BioContext(DbContextOptions<BioContext> options) : base(options)
        {
        }

        [UsedImplicitly] public DbSet<Site> Sites { get; set; }
        [UsedImplicitly] public DbSet<Tag> Tags { get; set; }
        [UsedImplicitly] public DbSet<Page> Pages { get; set; }

        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public DbSet<SettingsRecord> Settings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ForNpgsqlUseIdentityByDefaultColumns();

            RegisterJsonConversion<Section, StorageItem>(modelBuilder, s => s.Logo);
            RegisterJsonConversion<Section, StorageItem>(modelBuilder, s => s.LogoSmall);
            if (Database.IsInMemory())
            {
                RegisterSiteEntityConversions<Page, int>(modelBuilder);
            }

            modelBuilder.Entity<Section>().HasIndex(s => s.SiteIds);
            modelBuilder.Entity<Section>().HasIndex(s => s.IsPublished);
            modelBuilder.Entity<Section>().HasIndex(s => s.Url);

            modelBuilder.Entity<ContentItem>().HasIndex(i => i.SiteIds);
            modelBuilder.Entity<ContentItem>().HasIndex(i => i.TagIds);
            modelBuilder.Entity<ContentItem>().HasIndex(i => i.SectionIds);
            modelBuilder.Entity<ContentItem>().HasIndex(i => i.IsPublished);
            modelBuilder.Entity<ContentItem>().HasIndex(i => i.Type);
            modelBuilder.Entity<ContentItem>().HasIndex(i => i.Url);

            var dataConversionRegistrationMethod = GetType().GetMethod(nameof(RegisterDataConversion),
                BindingFlags.Instance | BindingFlags.NonPublic);
            var siteConversionsRegistrationMethod = GetType().GetMethod(nameof(RegisterSiteEntityConversions),
                BindingFlags.Instance | BindingFlags.NonPublic);
            var sectionConversionsRegistrationMethod = GetType().GetMethod(nameof(RegisterSectionEntityConversions),
                BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var sectionType in TypesProvider.GetSectionTypes())
            {
                Console.WriteLine($"Register section type {sectionType}");
                RegisterDiscriminator<Section, int>(modelBuilder, sectionType.type, sectionType.discriminator);
                dataConversionRegistrationMethod?.MakeGenericMethod(sectionType.type, sectionType.dataType)
                    .Invoke(this, new object[] {modelBuilder});
                if (Database.IsInMemory())
                {
                    siteConversionsRegistrationMethod?.MakeGenericMethod(sectionType.type,
                            sectionType.type.GetProperty("Id")?.PropertyType)
                        .Invoke(this, new object[] {modelBuilder});
                }
            }

            foreach (var contentType in TypesProvider.GetContentTypes())
            {
                Console.WriteLine($"Register content type {contentType}");
                RegisterDiscriminator<ContentItem, int>(modelBuilder, contentType.type,
                    contentType.discriminator);
                dataConversionRegistrationMethod?.MakeGenericMethod(contentType.type, contentType.dataType)
                    .Invoke(this, new object[] {modelBuilder});
                if (Database.IsInMemory())
                {
                    siteConversionsRegistrationMethod?.MakeGenericMethod(contentType.type,
                            contentType.type.GetProperty("Id")?.PropertyType)
                        .Invoke(this, new object[] {modelBuilder});
                    sectionConversionsRegistrationMethod?.MakeGenericMethod(contentType.type,
                            contentType.type.GetProperty("Id")?.PropertyType)
                        .Invoke(this, new object[] {modelBuilder});
                }
            }
        }

        private void RegisterDiscriminator<TBase, TDiscriminator>(ModelBuilder modelBuilder, Type sectionType,
            TDiscriminator discriminator) where TBase : class
        {
            var discriminatorBuilder =
                modelBuilder.Entity<TBase>().HasDiscriminator<TDiscriminator>(nameof(Section.Type));
            var method = discriminatorBuilder.GetType().GetMethods()
                .First(m => m.Name == nameof(DiscriminatorBuilder.HasValue) && m.IsGenericMethod);
            var typedMethod = method?.MakeGenericMethod(sectionType);
            typedMethod?.Invoke(discriminatorBuilder, new object[] {discriminator});
        }

        private void RegisterDataConversion<TEntity, TData>(ModelBuilder modelBuilder)
            where TEntity : class, ITypedEntity<TData> where TData : TypedData, new()
        {
            modelBuilder
                .Entity<TEntity>()
                .Property(e => e.Data)
                .HasColumnType("jsonb")
                .HasColumnName(nameof(ITypedEntity<TData>.Data))
                .HasConversion(
                    v => JsonConvert.SerializeObject(v),
                    v => JsonConvert.DeserializeObject<TData>(v));
        }

        private void RegisterJsonConversion<TEntity, TProperty>(ModelBuilder modelBuilder,
            Expression<Func<TEntity, TProperty>> propertySelector)
            where TEntity : class
        {
            modelBuilder
                .Entity<TEntity>()
                .Property(propertySelector)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonConvert.SerializeObject(v),
                    v => JsonConvert.DeserializeObject<TProperty>(v));
        }


        private void RegisterSiteEntityConversions<TEntity, TPk>(ModelBuilder modelBuilder)
            where TEntity : class, ISiteEntity<TPk>
        {
            modelBuilder
                .Entity<TEntity>()
                .Property(s => s.SiteIds)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonConvert.SerializeObject(v),
                    v => JsonConvert.DeserializeObject<int[]>(v));
        }

        private void RegisterSectionEntityConversions<TEntity, TPk>(ModelBuilder modelBuilder)
            where TEntity : class, ISectionEntity<TPk>
        {
            modelBuilder
                .Entity<TEntity>()
                .Property(s => s.SectionIds)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonConvert.SerializeObject(v),
                    v => JsonConvert.DeserializeObject<int[]>(v));
            modelBuilder
                .Entity<TEntity>()
                .Property(s => s.TagIds)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonConvert.SerializeObject(v),
                    v => JsonConvert.DeserializeObject<int[]>(v));
        }
    }
}