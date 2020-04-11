﻿// <auto-generated />
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Server.Models;

namespace Server.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn)
                .HasAnnotation("ProductVersion", "2.2.6-servicing-10079")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            modelBuilder.Entity("Server.Models.Match", b =>
                {
                    b.Property<long>("MatchId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint");

                    b.Property<string>("CustomGame")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<long>("Duration")
                        .HasColumnType("bigint");

                    b.Property<DateTime>("EndedAt")
                        .HasColumnType("timestamp without time zone");

                    b.Property<string>("MapName")
                        .HasColumnType("text");

                    b.Property<int>("Winner")
                        .HasColumnType("integer");

                    b.HasKey("MatchId");

                    b.ToTable("Matches");
                });

            modelBuilder.Entity("Server.Models.MatchEvent", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Body")
                        .HasColumnType("text");

                    b.Property<long>("MatchId")
                        .HasColumnType("bigint");

                    b.HasKey("Id");

                    b.ToTable("MatchEvents");
                });

            modelBuilder.Entity("Server.Models.MatchPlayer", b =>
                {
                    b.Property<long>("MatchId")
                        .HasColumnType("bigint");

                    b.Property<decimal>("SteamId")
                        .HasColumnType("numeric(20,0)");

                    b.Property<long>("Assists")
                        .HasColumnType("bigint");

                    b.Property<long>("Deaths")
                        .HasColumnType("bigint");

                    b.Property<string>("Hero")
                        .HasColumnType("text");

                    b.Property<long>("Kills")
                        .HasColumnType("bigint");

                    b.Property<long>("Level")
                        .HasColumnType("bigint");

                    b.Property<string>("PickReason")
                        .HasColumnType("text");

                    b.Property<int>("PlayerId")
                        .HasColumnType("integer");

                    b.Property<int>("Team")
                        .HasColumnType("integer");

                    b.HasKey("MatchId", "SteamId");

                    b.HasIndex("SteamId");

                    b.ToTable("MatchPlayer");
                });

            modelBuilder.Entity("Server.Models.Player", b =>
                {
                    b.Property<decimal>("SteamId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("numeric(20,0)");

                    b.Property<string>("Comment")
                        .HasColumnType("text");

                    b.Property<bool?>("PatreonBootsEnabled")
                        .HasColumnType("boolean");

                    b.Property<List<int>>("PatreonChatWheelFavorites")
                        .HasColumnType("integer[]");

                    b.Property<string>("PatreonEmblemColor")
                        .HasColumnType("text");

                    b.Property<bool?>("PatreonEmblemEnabled")
                        .HasColumnType("boolean");

                    b.Property<DateTime?>("PatreonEndDate")
                        .HasColumnType("timestamp without time zone");

                    b.Property<int>("PatreonLevel")
                        .HasColumnType("integer");

                    b.Property<int>("Rating")
                        .HasColumnType("integer");

                    b.HasKey("SteamId");

                    b.ToTable("Players");
                });

            modelBuilder.Entity("Server.Models.MatchPlayer", b =>
                {
                    b.HasOne("Server.Models.Match", "Match")
                        .WithMany("Players")
                        .HasForeignKey("MatchId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("Server.Models.Player", "Player")
                        .WithMany("Matches")
                        .HasForeignKey("SteamId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });
#pragma warning restore 612, 618
        }
    }
}
