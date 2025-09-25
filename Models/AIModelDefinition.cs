using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Photobooth.Models
{
    public class AIModelDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Provider { get; set; } // "replicate", "huggingface", etc.
        public string ModelPath { get; set; } // e.g., "google/nano-banana", "stability-ai/sdxl"
        public string ModelVersion { get; set; } // Optional specific version
        public ModelCapabilities Capabilities { get; set; }
        public ModelParameters DefaultParameters { get; set; }
        public bool SupportsImageInput { get; set; } = true;
        public bool PreservesIdentity { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Input format requirements
        public string ImageInputFormat { get; set; } = "base64"; // "base64", "url", "dataurl"
        public bool RequiresImageArray { get; set; } = false;

        // API configuration
        public bool SupportsSynchronousMode { get; set; } = false;
        public int DefaultTimeoutSeconds { get; set; } = 120;
    }

    public class ModelCapabilities
    {
        public bool SupportsNegativePrompt { get; set; } = true;
        public bool SupportsStrength { get; set; } = true;
        public bool SupportsGuidanceScale { get; set; } = true;
        public bool SupportsSteps { get; set; } = true;
        public bool SupportsScheduler { get; set; } = false;
        public bool SupportsSeed { get; set; } = true;
        public List<string> SupportedOutputFormats { get; set; } = new List<string> { "png", "jpg" };
        public List<string> SupportedSchedulers { get; set; } = new List<string>();
    }

    public class ModelParameters
    {
        public double Strength { get; set; } = 0.7;
        public double GuidanceScale { get; set; } = 7.5;
        public int Steps { get; set; } = 30;
        public string Scheduler { get; set; } = "DPMSolverMultistep";
        public int? Seed { get; set; }
        public string OutputFormat { get; set; } = "png";
        public int Width { get; set; } = 1024;
        public int Height { get; set; } = 1024;
    }

    public class AIModelTemplatePrompt
    {
        public string Id { get; set; }
        public string ModelId { get; set; }
        public string TemplateId { get; set; }
        public string Prompt { get; set; }
        public string NegativePrompt { get; set; }
        public ModelParameters Parameters { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public static class PredefinedModels
    {
        public static List<AIModelDefinition> GetDefaultModels()
        {
            return new List<AIModelDefinition>
            {
                new AIModelDefinition
                {
                    Id = "nano-banana",
                    Name = "Google Nano Banana",
                    Description = "Identity-preserving transformations - keeps the person's face unchanged",
                    Provider = "replicate",
                    ModelPath = "google/nano-banana",
                    PreservesIdentity = true,
                    SupportsSynchronousMode = true,
                    ImageInputFormat = "dataurl",
                    RequiresImageArray = true,
                    Capabilities = new ModelCapabilities
                    {
                        SupportsNegativePrompt = false,
                        SupportsStrength = false,
                        SupportsGuidanceScale = false,
                        SupportsSteps = false,
                        SupportsSeed = false
                    },
                    DefaultParameters = new ModelParameters
                    {
                        OutputFormat = "png"
                    }
                },
                new AIModelDefinition
                {
                    Id = "sdxl",
                    Name = "Stable Diffusion XL",
                    Description = "High-quality image transformations with extensive control",
                    Provider = "replicate",
                    ModelPath = "stability-ai/sdxl",
                    ModelVersion = "39ed52f2a78e934b3ba6e2a89f5b1c712de7dfea535525255b1aa35c5565e08b",
                    PreservesIdentity = false,
                    ImageInputFormat = "dataurl",
                    Capabilities = new ModelCapabilities
                    {
                        SupportsNegativePrompt = true,
                        SupportsStrength = true,
                        SupportsGuidanceScale = true,
                        SupportsSteps = true,
                        SupportsSeed = true,
                        SupportsScheduler = true,
                        SupportedSchedulers = new List<string>
                        {
                            "DPMSolverMultistep", "DDIM", "K_EULER", "K_EULER_ANCESTRAL"
                        }
                    },
                    DefaultParameters = new ModelParameters
                    {
                        Strength = 0.35,
                        GuidanceScale = 7.5,
                        Steps = 30,
                        Scheduler = "DPMSolverMultistep"
                    }
                },
                new AIModelDefinition
                {
                    Id = "instantid",
                    Name = "InstantID",
                    Description = "Face-preserving style transfer",
                    Provider = "replicate",
                    ModelPath = "zsxkib/instant-id",
                    ModelVersion = "083096c7e58e9edd8d836be696d90e96a91f9bb57a942e8ad967e0ca4e0a2078",
                    PreservesIdentity = true,
                    ImageInputFormat = "dataurl",
                    Capabilities = new ModelCapabilities
                    {
                        SupportsNegativePrompt = true,
                        SupportsStrength = true,
                        SupportsGuidanceScale = true,
                        SupportsSteps = true
                    },
                    DefaultParameters = new ModelParameters
                    {
                        Strength = 0.8,
                        GuidanceScale = 7.5,
                        Steps = 30
                    }
                },
                new AIModelDefinition
                {
                    Id = "rembg",
                    Name = "Background Remover",
                    Description = "Removes background from images",
                    Provider = "replicate",
                    ModelPath = "cjwbw/rembg",
                    ModelVersion = "fb8af171cfa1616ddcf1242c093f9c46bcada5ad4cf6f2fbe8b81b330ec5c003",
                    PreservesIdentity = true,
                    ImageInputFormat = "dataurl",
                    Capabilities = new ModelCapabilities
                    {
                        SupportsNegativePrompt = false,
                        SupportsStrength = false,
                        SupportsGuidanceScale = false,
                        SupportsSteps = false
                    },
                    DefaultParameters = new ModelParameters()
                },
                new AIModelDefinition
                {
                    Id = "controlnet",
                    Name = "ControlNet Canny",
                    Description = "Guided transformations using edge detection",
                    Provider = "replicate",
                    ModelPath = "stability-ai/sdxl-controlnet",
                    PreservesIdentity = false,
                    ImageInputFormat = "dataurl",
                    Capabilities = new ModelCapabilities
                    {
                        SupportsNegativePrompt = true,
                        SupportsStrength = true,
                        SupportsGuidanceScale = true,
                        SupportsSteps = true
                    },
                    DefaultParameters = new ModelParameters
                    {
                        Strength = 0.5,
                        GuidanceScale = 7.5,
                        Steps = 20
                    }
                },
                new AIModelDefinition
                {
                    Id = "seedream-4",
                    Name = "ByteDance Seedream-4",
                    Description = "Advanced text-to-image generation with high quality outputs",
                    Provider = "replicate",
                    ModelPath = "bytedance/seedream-4",
                    PreservesIdentity = false,
                    SupportsImageInput = false, // This is a text-to-image model
                    ImageInputFormat = "none", // No image input required
                    SupportsSynchronousMode = false,
                    Capabilities = new ModelCapabilities
                    {
                        SupportsNegativePrompt = false,
                        SupportsStrength = false,
                        SupportsGuidanceScale = false,
                        SupportsSteps = false,
                        SupportsSeed = false,
                        SupportedOutputFormats = new List<string> { "jpg", "png" }
                    },
                    DefaultParameters = new ModelParameters
                    {
                        OutputFormat = "jpg",
                        Width = 1024,
                        Height = 768 // Default 4:3 aspect ratio
                    }
                }
            };
        }
    }
}