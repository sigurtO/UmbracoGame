using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace UmbracoGame.Models
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Please enter a unique username.")]
        public string Username { get; set; } // This is their login AND display name

        [Required(ErrorMessage = "Please enter an email address.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email format.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Please enter a password.")]
        [MinLength(10, ErrorMessage = "Password must be at least 10 characters.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Please confirm your password.")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }
    }
}
