using VMS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VMS.Services
{
    /// <summary>
    /// Service for managing recipes (load, save, list)
    /// </summary>
    public class RecipeService
    {
        private static RecipeService? _instance;
        public static RecipeService Instance => _instance ??= new RecipeService();

        private readonly string _recipesDirectory;
        private Recipe? _currentRecipe;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public Recipe? CurrentRecipe => _currentRecipe;

        private RecipeService()
        {
            var configDir = ConfigurationService.Instance.ConfigDirectory;
            _recipesDirectory = Path.Combine(configDir, "Recipes");

            if (!Directory.Exists(_recipesDirectory))
            {
                Directory.CreateDirectory(_recipesDirectory);
            }
        }

        /// <summary>
        /// Load a recipe from file
        /// </summary>
        public Recipe? LoadRecipe(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonOptions);
                    if (recipe != null)
                    {
                        _currentRecipe = recipe;
                        return recipe;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading recipe: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Save a recipe to file
        /// </summary>
        public bool SaveRecipe(Recipe recipe, string? filePath = null)
        {
            try
            {
                recipe.ModifiedAt = DateTime.UtcNow;

                var path = filePath ?? GetRecipeFilePath(recipe.Id);
                var json = JsonSerializer.Serialize(recipe, JsonOptions);
                File.WriteAllText(path, json);

                _currentRecipe = recipe;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving recipe: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get list of all available recipes
        /// </summary>
        public List<RecipeInfo> GetRecipeList()
        {
            var recipes = new List<RecipeInfo>();

            try
            {
                var files = Directory.GetFiles(_recipesDirectory, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonOptions);
                        if (recipe != null)
                        {
                            recipes.Add(new RecipeInfo
                            {
                                Id = recipe.Id,
                                Name = recipe.Name,
                                Description = recipe.Description,
                                Version = recipe.Version,
                                ModifiedAt = recipe.ModifiedAt,
                                FilePath = file
                            });
                        }
                    }
                    catch
                    {
                        // Skip invalid recipe files
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error listing recipes: {ex.Message}");
            }

            return recipes;
        }

        /// <summary>
        /// Create a new empty recipe
        /// </summary>
        public Recipe CreateNewRecipe(string name = "New Recipe")
        {
            var recipe = new Recipe
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            };

            _currentRecipe = recipe;
            return recipe;
        }

        /// <summary>
        /// Delete a recipe by ID
        /// </summary>
        public bool DeleteRecipe(string id)
        {
            try
            {
                var path = GetRecipeFilePath(id);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    if (_currentRecipe?.Id == id)
                    {
                        _currentRecipe = null;
                    }
                    return true;
                }

                // Try to find by filename pattern
                var files = Directory.GetFiles(_recipesDirectory, $"*{id}*.json");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                return files.Length > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting recipe: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export a recipe to a specified path
        /// </summary>
        public bool ExportRecipe(Recipe recipe, string exportPath)
        {
            try
            {
                var json = JsonSerializer.Serialize(recipe, JsonOptions);
                File.WriteAllText(exportPath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error exporting recipe: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Import a recipe from an external path
        /// </summary>
        public Recipe? ImportRecipe(string importPath)
        {
            var recipe = LoadRecipe(importPath);
            if (recipe != null)
            {
                // Generate new ID to avoid conflicts
                recipe.Id = Guid.NewGuid().ToString();
                SaveRecipe(recipe);
            }
            return recipe;
        }

        /// <summary>
        /// Set the current active recipe
        /// </summary>
        public void SetCurrentRecipe(Recipe? recipe)
        {
            _currentRecipe = recipe;
        }

        private string GetRecipeFilePath(string recipeId)
        {
            return Path.Combine(_recipesDirectory, $"recipe_{recipeId}.json");
        }

        /// <summary>
        /// Get the recipes directory path
        /// </summary>
        public string RecipesDirectory => _recipesDirectory;
    }
}
