using OpenTraceability.Models.Identifiers;
using OpenTraceability.Utility;

namespace TraceabilityDriver.Models.Mapping;

public class CommonLocation : CommonBaseModel
{
    /// <summary>
    /// A unique identifier for the location.
    /// </summary>
    public string? LocationId { get; set; } = null;

    /// <summary>
    /// The ID of the owner of the location.
    /// </summary>
    public string? OwnerId { get; set; } = null;

    /// <summary>
    /// The registration number for the location.
    /// </summary>
    public string? RegistrationNumber { get; set; } = null;

    /// <summary>
    /// The name of the location.
    /// </summary>
    public string? Name { get; set; } = null;

    public string? Address1 { get; set; } = null;

    public string? Address2 { get; set; } = null;

    public string? City { get; set; } = null;

    public string? State { get; set; } = null;


    /// <summary>
    /// The country of the location.
    /// </summary>
    public Country? Country { get; set; } = null;

    /// <summary>
    /// Merges the location onto this one. Properties are only merged if they are null.
    /// </summary>
    /// <param name="other">The other location.</param>
    public void Merge(CommonLocation other)
    {
        if (this.LocationId == null && other.LocationId != null)
        {
            this.LocationId = other.LocationId;
        }

        if (this.RegistrationNumber == null && other.RegistrationNumber != null)
        {
            this.RegistrationNumber = other.RegistrationNumber;
        }

        if (this.Name == null && other.Name != null)
        {
            this.Name = other.Name;
        }

        if(this.Address1 == null && other.Address1 != null)
        {
            this.Address1 = other.Address1;
        }
        if(this.Address2 == null && other.Address2 != null)
        {
            this.Address2 = other.Address2;
        }
        if(this.City == null && other.City != null)
        {
            this.City = other.City;
        }
        if(this.State == null && other.State != null)
        {
            this.State = other.State;
        }

        if (this.Country == null && other.Country != null)
        {
            this.Country = other.Country;
        }
    }

    /// <summary>
    /// This method will attempt to generate a GTIN for the product definition.
    /// </summary>
    /// <returns></returns>
    public GLN GetGLN()
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(this.LocationId);

        // If the location ID is already a GLN, then just parse that and return it.
        if (GLN.IsGLN(this.LocationId))
        {
            return new GLN(this.LocationId);
        }
        else
        {
            // Ensure we have an owner ID set.
            ArgumentNullException.ThrowIfNullOrWhiteSpace(this.OwnerId);

            // Generate the GTIN.
            string gln = $"urn:gdst:{GDST_IDENTIFIERS_DOMAIN}:location:loc:{NormalizeString(this.OwnerId)}.{NormalizeString(this.LocationId)}";
            return new GLN(gln);
        }
    }
}


