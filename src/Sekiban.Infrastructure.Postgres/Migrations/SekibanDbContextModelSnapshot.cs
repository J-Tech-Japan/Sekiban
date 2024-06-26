﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Sekiban.Infrastructure.Postgres.Databases;

#nullable disable

namespace Sekiban.Infrastructure.Postgres.Migrations
{
    [DbContext(typeof(SekibanDbContext))]
    partial class SekibanDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.2")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Sekiban.Infrastructure.Postgres.Databases.DbCommandDocument", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<int>("AggregateContainerGroup")
                        .HasColumnType("integer");

                    b.Property<Guid>("AggregateId")
                        .HasColumnType("uuid");

                    b.Property<string>("AggregateType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("CallHistories")
                        .IsRequired()
                        .HasColumnType("json");

                    b.Property<int>("DocumentType")
                        .HasColumnType("integer");

                    b.Property<string>("DocumentTypeName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Exception")
                        .HasColumnType("text");

                    b.Property<string>("ExecutedUser")
                        .HasColumnType("text");

                    b.Property<string>("PartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("json");

                    b.Property<string>("RootPartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("SortableUniqueId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.ToTable("Commands");
                });

            modelBuilder.Entity("Sekiban.Infrastructure.Postgres.Databases.DbDissolvableEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("AggregateId")
                        .HasColumnType("uuid");

                    b.Property<string>("AggregateType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("CallHistories")
                        .IsRequired()
                        .HasColumnType("json");

                    b.Property<int>("DocumentType")
                        .HasColumnType("integer");

                    b.Property<string>("DocumentTypeName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("json");

                    b.Property<string>("RootPartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("SortableUniqueId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Version")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.ToTable("DissolvableEvents");
                });

            modelBuilder.Entity("Sekiban.Infrastructure.Postgres.Databases.DbEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<Guid>("AggregateId")
                        .HasColumnType("uuid");

                    b.Property<string>("AggregateType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("CallHistories")
                        .IsRequired()
                        .HasColumnType("json");

                    b.Property<int>("DocumentType")
                        .HasColumnType("integer");

                    b.Property<string>("DocumentTypeName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("json");

                    b.Property<string>("RootPartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("SortableUniqueId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Version")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.ToTable("Events");
                });

            modelBuilder.Entity("Sekiban.Infrastructure.Postgres.Databases.DbMultiProjectionDocument", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<int>("AggregateContainerGroup")
                        .HasColumnType("integer");

                    b.Property<string>("AggregateType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int>("DocumentType")
                        .HasColumnType("integer");

                    b.Property<string>("DocumentTypeName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("LastEventId")
                        .HasColumnType("uuid");

                    b.Property<string>("LastSortableUniqueId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PayloadVersionIdentifier")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("RootPartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int>("SavedVersion")
                        .HasColumnType("integer");

                    b.Property<string>("SortableUniqueId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.ToTable("MultiProjectionSnapshots");
                });

            modelBuilder.Entity("Sekiban.Infrastructure.Postgres.Databases.DbSingleProjectionSnapshotDocument", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<int>("AggregateContainerGroup")
                        .HasColumnType("integer");

                    b.Property<Guid>("AggregateId")
                        .HasColumnType("uuid");

                    b.Property<string>("AggregateType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int>("DocumentType")
                        .HasColumnType("integer");

                    b.Property<string>("DocumentTypeName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("LastEventId")
                        .HasColumnType("uuid");

                    b.Property<string>("LastSortableUniqueId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PayloadVersionIdentifier")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("RootPartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int>("SavedVersion")
                        .HasColumnType("integer");

                    b.Property<string>("Snapshot")
                        .HasColumnType("json");

                    b.Property<string>("SortableUniqueId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("TimeStamp")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.ToTable("SingleProjectionSnapshots");
                });
#pragma warning restore 612, 618
        }
    }
}
