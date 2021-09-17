using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NDesk.Options;
using FileToVoxCore.Vox;

namespace VoxExporter
{
	class Program
	{
		static void Main(string[] args)
		{
			string inputFile = string.Empty;
			string outputFile = string.Empty;
			bool shopHelp = false;
			bool debug = false;
			bool logs = false;
			OptionSet options = new OptionSet()
			{
				{"i|input=", "input file", v => inputFile = v},
				{"o|output=", "output file", v => outputFile = v},
				{"h|help", "show this message and exit", v => shopHelp = v != null},
				{"d|debug", "enable debug verification", v => debug = v != null },
				{"l|log", "enable writing logs", v =>  logs = v != null}
			};

			List<string> extra = options.Parse(args);
			DisplayInformations();
			CheckHelp(options, shopHelp);
			CheckArguments(inputFile, outputFile);
			ProcessFile(inputFile, outputFile, logs, debug);

		}

		private static void DisplayInformations()
		{
			Console.WriteLine("[INFO] VoxMerger v" + Assembly.GetExecutingAssembly().GetName().Version);
			Console.WriteLine("[INFO] Author: @Zarbuz. Contact : https://twitter.com/Zarbuz");
		}

		private static void ProcessFile(string inputFile, string outputFile, bool logs, bool debug)
		{
			VoxReader reader = new VoxReader();

			Console.WriteLine("[LOG] Started to load model: " + inputFile);
			VoxModel model = reader.LoadModel(inputFile);

			VoxWriterCustom writer = new VoxWriterCustom();
			outputFile = outputFile.Replace(".vox", "");
			writer.WriteModel(outputFile, model);
			//reader.LoadModel(outputFile, logs, debug);
		}

		private static void CheckArguments(string inputFile, string outputFile)
		{
			if (string.IsNullOrEmpty(inputFile))
			{
				Console.WriteLine("[ERR] Missing input file path. Check help for more informations.");
				Environment.Exit(1);
			}
			else if (Path.GetExtension(inputFile) != ".vox")
			{
				Console.WriteLine("[ERR] Input file is not a .vox file");
				Environment.Exit(1);
			}

			if (string.IsNullOrEmpty(outputFile))
			{
				Console.WriteLine("[ERR] Missing output file path. Check help for more informations.");
				Environment.Exit(1);
			}
		}

		private static void CheckHelp(OptionSet options, bool shopHelp)
		{
			if (shopHelp)
			{
				ShowHelp(options);
				Environment.Exit(0);
			}
		}

		private static void ShowHelp(OptionSet p)
		{
			Console.WriteLine("Usage: VoxExporter --i INPUT --o OUTPUT");
			Console.WriteLine("Options: ");
			p.WriteOptionDescriptions(Console.Out);
		}

	}
}
