using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Newtonsoft.Json;
using SkiShift.Application.DTOs;
using SkiShift.Code.Services;
using SkiShift.Constants;
using SkiShift.Extensions;
using SkiShift.Models.DTOs;
using SkiShift.Models.Enums;
using SkiShift.Models.ResponseModels;
using SkiShift.Models.StorageModels;
using SkiShift.Models.StorageModels.MongoModels;
using Swashbuckle.AspNetCore.Annotations;
using Telegram.Bot;
using Guid = System.Guid;

namespace SkiShift.Controllers
{

    [ApiController]
    [ApiVersion(ApiConstants.Versions.V1_0)]
    [Route(Controller)]
    [SwaggerTag("Create, read, update and delete Parcel")]
    [Authorize(AuthenticationSchemes = "Bearer")]

    public class ParcelController : ControllerBase
    {
        #region Private fields
        private dynamic _ret;
        private HttpStatusCode _code;
        private readonly IParcelService _parcelService;
        private readonly ILogger<ParcelController> _logger;
        private readonly UnitOfWork _unitOfWork;
        private readonly TelegramBotClient _telegramBot;
        private readonly IMapper _autoMapper;
        private const string Controller = ApiConstants.Endpoints.DefaultVersionedRoute + "parcel/";
        #endregion


        #region 
        public ParcelController(IParcelService parcelService,
            ILogger<ParcelController> logger, UnitOfWork unitOfWork, IMapper autoMapper)
        {
            _parcelService = parcelService;
            _logger = logger;
            _unitOfWork = unitOfWork;
            _telegramBot = new TelegramBotClient("2003623788:AAHdHFgu3yG7hrTYMuTElcQNibMb-qbFcPM");
            _autoMapper = autoMapper;
        }
        #endregion
        /// <summary>
        /// Endpoint returns parcel by id.
        /// </summary>
        /// <param name="parcelId"></param>
        /// <returns></returns>
        [HttpGet]
        [SwaggerOperation(Summary = "Get all deals ", Description = "Get all parcels.")]
        [Authorize(Roles = "b2b, admin, client")]
        [Route("findOneParcelById")]
        public async Task<ActionResult<ParcelResponseDto>> FindOneParcelById([FromQuery] Guid parcelId)
        {
            try
            {
                _code = HttpStatusCode.OK;

                var parcelFromDataBase = await _parcelService.GetParcelById(parcelId);

                _ret = _autoMapper.Map<ParcelResponseDto>(parcelFromDataBase);

                if (_ret == null)
                {
                    _code = HttpStatusCode.NoContent;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new {err = ex.ToString()};

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var response = new ObjectResult(_ret) {StatusCode = (int) _code};

            return response;
        }
        /// <summary>
        /// Endpoint returns parcels for current user.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("getAllFilteredByCurrentUser")]
        [SwaggerOperation(Summary = "Get all parcels (Parcels) for current user.",
            Description = "Get all parcels for current user.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult<ParcelResponseDto>> GetAllFilteredByCurrentUser()
        {

            try
            {
                //3f7d416f-879d-41be-b411-2a08db977aa6
                //getting current user guid...:
                var currentUserId = new Guid(User.GetUid());

                var filterParcelsByUserId = Builders<ParcelDalDto>.Filter.Eq("clientId", currentUserId);

                var parcelCollection =
                    (await _unitOfWork.GetCollection<ParcelDalDto>("Parcel").FindAsync(filterParcelsByUserId)).ToList();

                var filterDealsByUserId = Builders<DealDalDto>.Filter.Eq("offers.clientId", currentUserId);

                var unitedModelResponse = (await _unitOfWork.GetCollection<DealDalDto>("Deal").FindAsync(filterDealsByUserId))
                    .ToList().Select(dealModel => new DealOffersWithParcelsDto()
                    {
                        Id = dealModel.Id,
                        ShifterId = dealModel.ShifterId,
                        DealStatus = dealModel.DealStatus,
                        DealDescription = dealModel.DealDescription,
                        OfferCharacteristicsList = 
                            dealModel.Offers
                                .Where(x => x.ClientId == currentUserId)
                                .ToList().GroupJoin(parcelCollection, offer => offer.Id, package =>
                                        package.OfferId,
                                    (offer, parcels) => _parcelService.MapOfferModel(parcels, offer)
                                ).ToList(),
                        DealBaggageType = dealModel.BaggageType,
                        DealTransport = dealModel.Transport,
                        DealStartDate = dealModel?.StartDate?.ConvertDateFromDalToString(),
                        DealEndDate = dealModel?.EndDate?.ConvertDateFromDalToString(),
                        DealAddressFrom = dealModel.AddressFrom,
                        DealAddressTo = dealModel.AddressTo,
                        LastModified = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss")
                    }).ToList();

                _ret = unitedModelResponse;

                if (_ret == null)
                {
                    _ret = "No parcels by user id were found.";

                    _code = HttpStatusCode.NoContent;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new {err = ex.ToString()};

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var response = new ObjectResult(_ret) {StatusCode = (int) _code};

            return response;
        }

        /// <summary>
        /// Endpoint returns all parcels for admin.
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("getAllForAdmin")]
        [SwaggerOperation(Summary = "Get all parcels (Parcels) for current user.",
            Description = "Get all parcels for current user.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult<ParcelResponseDto>> GetAllForAdmin()
        {
            try
            {
                var parcelsFromDatabase = (await _unitOfWork.GetCollection<ParcelDalDto>("Parcel").FindAsync(_ => true)).ToList()
                    .Select(item => _autoMapper.Map<ParcelResponseDto>(item)).ToList();

                var idOfClientsFromParcels = parcelsFromDatabase.Select(x => x.ClientId);

                var filterToFindProfilesOfClients = Builders<UserProfileSegmentDalDto>.Filter.In("_id", idOfClientsFromParcels);

                var profiles = (await _unitOfWork.GetCollection<UserProfileSegmentDalDto>("UserProfile").FindAsync(filterToFindProfilesOfClients)).ToList();

                if (parcelsFromDatabase.Any())
                {
                    var listResponse = new List<ParcelWithClientDto>();

                    foreach (var item in parcelsFromDatabase)
                    {
                        var parcelAndClient = new ParcelWithClientDto();

                        parcelAndClient.Id = item.Id;
                        parcelAndClient.CreatedOn = item.CreatedOn;
                        parcelAndClient.ClientId = item.ClientId;
                        parcelAndClient.OfferId = item.OfferId;
                        parcelAndClient.AddressFrom = item.AddressFrom;
                        parcelAndClient.AddressTo = item.AddressTo;
                        parcelAndClient.LocationTo = item.LocationTo;
                        parcelAndClient.DepartureDateTime = item.DepartureDateTime;
                        parcelAndClient.BaggageTypes = item.BaggageTypes;
                        parcelAndClient.ParcelSum = item.ParcelSum;
                        parcelAndClient.ParcelDescription = item.ParcelDescription;
                        parcelAndClient.LastModified = item.LastModified;

                        var currentProfile = profiles.FirstOrDefault(x => x.Id == item.ClientId);

                        if (currentProfile!=null)
                        {
                            parcelAndClient.UserName = currentProfile.UserName;
                            parcelAndClient.FirstName = currentProfile.FirstName;
                            parcelAndClient.SecondName = currentProfile.SecondName;
                            parcelAndClient.ThirdName = currentProfile.ThirdName;
                            parcelAndClient.PhoneNumber = currentProfile.PhoneNumber;

                            var avatar = currentProfile.Documents?.FirstOrDefault(x => x.PersonalDocument.Type == TypeOfDocument.Avatar)?.Url;

                            parcelAndClient.ClientAvatarUrl =  avatar.IsNullOrEmpty()? string.Empty : avatar;
                        }

                        listResponse.Add(parcelAndClient);
                    }

                    _ret = listResponse;

                    _code = HttpStatusCode.OK;
                }
                else 
                {
                    _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                    _code = HttpStatusCode.NoContent;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new {err = ex.ToString()};

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var response = new ObjectResult(_ret) {StatusCode = (int) _code};

            return response;
        }

        /// <summary>
        /// Endpoint updates parcel by id.
        /// </summary>
        /// <param name="parcel"></param>
        /// <returns></returns>
        [HttpPatch]
        [SwaggerOperation(
            Summary = "Update Parcel",
            Description = "Update Parcel"
        )]
        public async Task<ActionResult> UpdateParcel([FromBody] Parcel parcel)
        {
            try
            {
                _code = HttpStatusCode.OK;
                if (!ModelState.IsValid) throw new Exception(ModelState.Serialize());
                var pr = await _parcelService.GetParcelById(parcel.Id);
                if (pr != null)
                {
                    var res = new Parcel()
                    {
                        Id = pr.Id,
                        AddressFrom = parcel.AddressFrom,
                        AddressTo = parcel.AddressTo,
                        LocationFrom = parcel.LocationFrom,
                        LocationTo = parcel.LocationTo,
                        ClientId = pr.ClientId,
                        BaggageTypes = parcel.BaggageTypes,
                        ParcelSum = parcel.ParcelSum,
                        ParcelDescription = parcel.ParcelDescription,
                        LastModified = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss")
                    };
                    if (res.Id != null) await _parcelService.UpdateById(res.Id.Value, res);
                    _ret = new {resp = "Package updated/created"};
                }
                else
                {
                    _code = HttpStatusCode.NoContent;
                    _ret = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new {err = ex.ToString()};

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var response = new ObjectResult(_ret) {StatusCode = (int) _code};

            return response;
        }

        /// <summary>
        /// Endpoint creates parcel by id.
        /// </summary>
        /// <param name="parcelDto"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("createTheParcel")]
        [SwaggerOperation(
            Summary = "Create new Parcel",
            Description = "Create new Parcel"
        )]
        public async Task<ActionResult> CreateTheParcel([FromBody] ParcelCreateDto parcelDto)
        {
            var newParcelToSave = _autoMapper.Map<Parcel>(parcelDto);

            var currentSessionUserId = new Guid(User.GetUid());

            newParcelToSave.Id = Guid.NewGuid();

            newParcelToSave.OfferId = Guid.NewGuid(); //crutch

            newParcelToSave.ClientId = currentSessionUserId;

            try
            {
                await _parcelService.Create(newParcelToSave);

                _code = HttpStatusCode.OK;

                _ret = newParcelToSave.Id;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new {err = ex.ToString()};

                _code = HttpStatusCode.InternalServerError;
            }

            var response = new ObjectResult(_ret) {StatusCode = (int) _code};

            return response;
        }
        
        #region return deal model to subscribe

        ///<summary>Creates deal and subscribes parcel to it.</summary>
        ///<response code="200">Status ok.</response>
        ///<response code="500">Internal server error.</response>
        ///<param name="parcelId">Model with search parameters.</param>
        [HttpGet]
        [Route("getDealFromParcel")]
        [SwaggerOperation(
            Summary = "Creates the form of the deal from parcel data.",
            Description = "Creates the pre form of the deal."
        )]
        public async Task<ObjectResult> CreateFormOfTheDeal([FromQuery] string parcelId)
        {
            try
            {
                var filterParcelsByParcelId = Builders<ParcelDalDto>.Filter.Eq("_id", parcelId);

                var parcelFromDataBase =
                    (await _unitOfWork.GetCollection<ParcelDalDto>("Parcel").FindAsync(filterParcelsByParcelId)).FirstOrDefault();

                if (parcelFromDataBase != null)
                {
                    var response = _parcelService.CreateDealPreForm(parcelFromDataBase);

                    _ret = response;

                    _code = HttpStatusCode.OK;
                }
                else
                {
                    _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                    _code = HttpStatusCode.NoContent;
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new {err = ex.ToString()};

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var result = new ObjectResult(_ret) {StatusCode = (int) _code};

            return result;
        }

        #endregion

        #region get parcel from deal
        ///<summary>Creates pre form of the parcel.</summary>
        ///<response code="200">Status ok.</response>
        ///<response code="500">Internal server error.</response>
        ///<param name="dealId">Model with search parameters.</param>
        [HttpGet]
        [Route("getParcelFromDeal")]
        [SwaggerOperation(
            Summary = "Creates the form of the parcel from deal data.",
            Description = "Creates pre form of the parcel."
        )]
        public async Task<ObjectResult> CreateFormOfTheParcel([FromQuery] string dealId)
        {
            try
            {
                var filterDealsByDealId = Builders<DealDalDto>.Filter.Eq("_id", dealId);

                var dealFromDataBase = (await _unitOfWork.GetCollection<DealDalDto>("Deal").FindAsync(filterDealsByDealId)).FirstOrDefault();

                if (dealFromDataBase != null)
                {
                    _ret = _parcelService.CreateParcelPreForm(dealFromDataBase);

                    _code = HttpStatusCode.OK;
                }
                else
                {
                    _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                    _code = HttpStatusCode.NoContent;
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }

        #endregion

        #region return list of parcels
        ///<summary>Returns list of linked parcels only by deal id.</summary>
        ///<response code="200">Status ok.</response>
        ///<response code="500">Internal server error.</response>
        ///<response code="204">Internal server error.</response>
        ///<param name="dealId">Model with search parameters.</param>
        [HttpGet]
        [Route("getParcelsByDealId")]
        [SwaggerOperation(
            Summary = "Get parcels by deal id.",
            Description = "Get parcels by deal id."
        )]
        public async Task<ObjectResult> GetParcelsByDealId([FromQuery] Guid dealId)
        {
            try
            {
                var filterDealsByDealId = Builders<DealDalDto>.Filter.Eq("_id", dealId);

                var dealDal = (await _unitOfWork.GetCollection<DealDalDto>("Deal").FindAsync(filterDealsByDealId))
                    .FirstOrDefault();

                if (dealDal!=null)
                {
                    var offerGuidList = dealDal.Offers.Select(z => z.Id).ToList();

                    var filterForParcels = Builders<ParcelDalDto>.Filter.In("offerId", offerGuidList);

                    var parcelsCollection = (await _unitOfWork.GetCollection<ParcelDalDto>("Parcel").FindAsync(filterForParcels)).ToList();

                    if (parcelsCollection.Any())
                    {
                        _ret = parcelsCollection.Select(s => _autoMapper.Map<ParcelResponseDto>(s));

                        _code = HttpStatusCode.OK;
                    }
                    else
                    {
                        _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                        _code = HttpStatusCode.NoContent;
                    }
                }
                else
                {
                    _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                    _code = HttpStatusCode.BadRequest;
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
        #endregion

        /// <summary>
        /// Endpoint subscribes parcel to the deal from client side.
        /// </summary>
        /// <param name="modelOfPostSubscription"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("subscribeAsClient")]
        [SwaggerOperation(Summary = "Subscribe batch of parcels to the deal.", Description = "Client choose many parcels and subscribes them to the single deal at once.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult> SubscribeParcelsAsAClient([FromBody] PostParcelSubscription modelOfPostSubscription)
        {
            if (modelOfPostSubscription.DealId != Guid.Empty)
            {
                var currentUserId = new Guid(User.GetUid());

                var resultOfSubscription = await _parcelService.SubscribeParcelsAsync(modelOfPostSubscription, currentUserId);

                if (resultOfSubscription.OfferStatus != OfferStatuses.CancelClient)
                {
                    _ret = JsonConvert.SerializeObject(new ResponseSuccess());

                    _code = HttpStatusCode.OK;
                }

                else
                {
                    _ret = JsonConvert.SerializeObject(new ResponseSubscription()
                    { Response = "no success", OfferStatus = resultOfSubscription.OfferStatus });

                    _code = HttpStatusCode.BadRequest;
                }
            }

            else
            {
                _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                _code = HttpStatusCode.InternalServerError;
            }

            return new ObjectResult(_ret) { StatusCode = (int)_code };
        }

        /// <summary>
        /// Endpoint subscribes parcel to the deal as a shipper.
        /// </summary>
        /// <param name="modelOfPostSubscription"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("subscribeAsShifter")]
        [SwaggerOperation(Summary = "Subscribe batch of parcels to the deal.",
            Description = "Shifter choose many parcels and subscribes them to the single deal at once.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult> SubscribeParcelsAsAShifter(
            [FromBody] PostParcelSubscription modelOfPostSubscription)
        {
            try
            {
                var currentUserId = new Guid(User.GetUid());

                //bulkSubscription:
                foreach (var parcelId in modelOfPostSubscription.ParcelIdsCollection)
                {
                    var offerToInsert = new OfferDalDto();

                    offerToInsert.LastModified = DateTime.Now.ToString("dddd, dd MMMM yyyy HH:mm:ss");

                    offerToInsert.OfferStatus = OfferStatuses.AgreeShifter;

                    offerToInsert.WhoCreator = WhoCreator.ShifterFirst;

                    offerToInsert.ClientId = currentUserId;

                    offerToInsert.Description = "offer from shifter";

                    offerToInsert.Id = Guid.NewGuid();

                    var filterParcel = Builders<ParcelDalDto>.Filter.Eq("_id", parcelId);

                    // 1 parcel
                    var parcelDal = (await _unitOfWork.GetCollection<ParcelDalDto>("Parcel")
                        .FindAsync(filterParcel))
                        .FirstOrDefault();

                    offerToInsert.OfferSum = parcelDal.ParcelSum;

                    offerToInsert.ParcelId = parcelDal.Id;

                    // 2 deal update
                    var filterDeal = Builders<DealDalDto>.Filter.Eq("_id", modelOfPostSubscription.DealId);

                    await _unitOfWork.GetCollection<DealDalDto>("Deal").UpdateOneAsync(filterDeal, Builders<DealDalDto>
                        .Update.AddToSet(x => x.Offers, offerToInsert));

                    _ret = JsonConvert.SerializeObject(new ResponseSuccess());

                    _code = HttpStatusCode.OK;
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = new { err = ex.ToString() };

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var response = new ObjectResult(_ret) { StatusCode = (int)_code };

            return response;
        }

        /// <summary>
        /// Endpoint finds deals by actuality and parcel id.
        /// </summary>
        /// <param name="statusData"></param>
        /// <param name="parcelId"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("findOffersByDealActualityAndParcelId")]
        [SwaggerOperation(
            Summary = "Get all active/inactive offers by parcelId.",
            Description = "Get all active/inactive offers by parcelId.")]
        [Authorize(Roles = "b2b, admin, client")]
        public async Task<ActionResult> FindOffersByDealActualityAndParcelId(
            [FromQuery] StatusData statusData,
            [FromQuery] Guid parcelId)
        {
            try
            {
                var dealDal = new DealDalDto();

                var dealStatusesList = statusData == StatusData.Current ? dealDal.ActiveDealFilter : dealDal.ArchiveDealFilter;

                var filterToFindDeals =
                    Builders<DealDalDto>.Filter.Eq("offers.parcelId", parcelId) &
                    Builders<DealDalDto>.Filter.In("dealStatus", dealStatusesList);

                var offerList =
                    (await _unitOfWork.GetCollection<DealDalDto>("Deal")
                        .Find(filterToFindDeals)
                        .ToListAsync())
                    .SelectMany(x => x.Offers)
                    .Where(x => x.ParcelId == parcelId).ToList()
                    .Select(x => _autoMapper.Map<OfferResponseDto>(x))
                    .ToList();

                if (offerList.Any())
                {
                    _ret = offerList;

                    _code = HttpStatusCode.OK;
                }
                else
                {
                    _ret = JsonConvert.SerializeObject(new ResponseNoSuccess());

                    _code = HttpStatusCode.NoContent;
                }
            }

            catch (Exception ex)
            {
                _logger.LogError(ex.StackTrace);

                _ret = ex.ToString();

                _code = HttpStatusCode.InternalServerError;

                throw;
            }

            var result = new ObjectResult(_ret) { StatusCode = (int)_code };

            return result;
        }
    }
}