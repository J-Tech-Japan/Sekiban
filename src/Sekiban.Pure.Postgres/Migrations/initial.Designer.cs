﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Sekiban.Pure.Postgres;

#nullable disable

namespace Sekiban.Pure.Postgres.Migrations
{
    [DbContext(typeof(SekibanDbContext))]
    [Migration("20250211221011_initial")]
    partial class initial
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.1")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Sekiban.Pure.Postgres.DbEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid");

                    b.Property<string>("AggregateGroup")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("AggregateId")
                        .HasColumnType("uuid");

                    b.Property<string>("CausationId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("CorrelationId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("ExecutedUser")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PartitionKey")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Payload")
                        .IsRequired()
                        .HasColumnType("json");

                    b.Property<string>("PayloadTypeName")
                        .IsRequired()
                        .HasColumnType("text");

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
#pragma warning restore 612, 618
        }
    }
}
