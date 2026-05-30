namespace Page_API.Models
{
    using System.ComponentModel.DataAnnotations;

    public class CreatePostRequest
    {
        [Required]
        [MinLength(1)]
        public string Message { get; set; } = string.Empty;
    }
}
