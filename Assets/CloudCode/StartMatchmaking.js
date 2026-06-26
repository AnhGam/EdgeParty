// StartMatchmaking.js
// UGS Cloud Code script - Creates/polls/cancels an Edgegap Matchmaking Ticket
// Uses the Ticket API (modern Edgegap Matchmaker API):
//   POST   {OM_BASE_URL}/v1/tickets                 → create ticket, returns { id, status, ... }
//   GET    {OM_BASE_URL}/v1/tickets/{ticketId}       → poll status
//   DELETE {OM_BASE_URL}/v1/tickets/{ticketId}       → cancel ticket
//
// Request params from client:
//   pings     : { [beaconName: string]: number }  - measured latencies in ms (omit = no latency preference)
//   ticketId  : string | null                     - existing ticket ID to poll (null = create new)
//   cancel    : boolean                            - true = cancel the ticket and return

const axios = require("axios");

const OM_BASE_URL = "https://om-ffn6c6ga6e.edgegap.net";

// Name of the matchmaking profile configured in your Edgegap matchmaker dashboard
const MATCHMAKING_PROFILE = "simple-example";

module.exports = async ({ params, context, logger, secretManager }) => {
  logger.info("StartMatchmaking: params received = " + JSON.stringify(params || {}));

  const pings = params.pings || params.Pings || params.PINGS;
  const ticketId = params.ticketId || params.TicketId || params.TICKETID;
  const cancel = params.cancel || params.Cancel || params.CANCEL;
  const players = params.players || params.Players || params.PLAYERS;
  const playerIp = params.playerIp || params.PlayerIp || params.PLAYERIP || null;

  // Print process.env keys to help debug environment variable configuration
  try {
    const envKeys = Object.keys(process.env || {});
    logger.info("StartMatchmaking: process.env keys = " + envKeys.join(", "));
  } catch (e) {
    logger.info("StartMatchmaking: Could not read process.env keys: " + e.message);
  }

  // Resolve token from process.env or UGS Secret Manager
  let token = process.env.OM_AUTH_TOKEN || process.env.EDGEGAP_AUTH_TOKEN || process.env.OM_TOKEN || process.env.EDGEGAP_TOKEN;
  if (!token && secretManager) {
    try {
      const secret = await secretManager.getSecret("EDGEGAP_AUTH_TOKEN");
      if (secret && secret.value) {
        token = secret.value;
        logger.info("StartMatchmaking: Resolved token from UGS Secret Manager (EDGEGAP_AUTH_TOKEN).");
      }
    } catch (e) {
      logger.info("StartMatchmaking: Secret Manager lookup for EDGEGAP_AUTH_TOKEN failed: " + e.message);
    }

    if (!token) {
      try {
        const secret = await secretManager.getSecret("OM_AUTH_TOKEN");
        if (secret && secret.value) {
          token = secret.value;
          logger.info("StartMatchmaking: Resolved token from UGS Secret Manager (OM_AUTH_TOKEN).");
        }
      } catch (e) {
        logger.info("StartMatchmaking: Secret Manager lookup for OM_AUTH_TOKEN failed: " + e.message);
      }
    }
  }

  if (!token) {
    logger.error("StartMatchmaking: Authentication token is missing/undefined.");
    throw new Error("Missing authentication token. Access denied.");
  }

  const headers = {
    Authorization: token,
    "Content-Type": "application/json",
  };

  // ── CANCEL ─────────────────────────────────────────────────────────────────
  if (cancel === true && ticketId) {
    logger.info(`StartMatchmaking: Cancelling ticket ${ticketId}...`);
    try {
      const ids = ticketId.split(";");
      for (const id of ids) {
        if (!id) continue;
        try {
          await axios.delete(`${OM_BASE_URL}/tickets/${id}`, {
            headers,
            timeout: 10000,
          });
          logger.info(`StartMatchmaking: Ticket ${id} cancelled successfully.`);
        } catch (err) {
          if (err.response && err.response.status === 404) {
            logger.info(`StartMatchmaking: Ticket ${id} not found (already removed).`);
          } else {
            logger.warn(`StartMatchmaking: Failed to cancel ticket ${id}: ${err.message}`);
          }
        }
      }
      return JSON.stringify({ status: "CANCELLED" });
    } catch (error) {
      const errMsg = error.response ? JSON.stringify(error.response.data) : error.message;
      logger.error(`StartMatchmaking: Cancel error - ${errMsg}`);
      throw new Error(`Edgegap API error: ${error.response ? error.response.status : "N/A"} - ${errMsg}`);
    }
  }

  // ── POLL (ticketId provided, not cancelling) ────────────────────────────────
  if (ticketId && cancel !== true) {
    logger.info(`StartMatchmaking: Polling ticket ${ticketId}...`);
    try {
      const ids = ticketId.split(";");
      const primaryId = ids[0];
      const response = await axios.get(`${OM_BASE_URL}/tickets/${primaryId}`, {
        headers,
        timeout: 10000,
      });

      const ticket = response.data;
      logger.info(`StartMatchmaking: Ticket status = ${ticket.status}`);
      // Restore the combined ticket ID string so client continues to poll with it
      ticket.id = ticketId;
      return JSON.stringify(ticket);
    } catch (error) {
      if (error.response && error.response.status === 404) {
        logger.info(`StartMatchmaking: Primary ticket not found (expired/removed).`);
        return JSON.stringify({ status: "CANCELLED" });
      }
      const errMsg = error.response ? JSON.stringify(error.response.data) : error.message;
      logger.error(`StartMatchmaking: Poll error - ${errMsg}`);
      throw new Error(`Edgegap API error: ${error.response ? error.response.status : "N/A"} - ${errMsg}`);
    }
  }

  // ── CREATE NEW TICKET ───────────────────────────────────────────────────────
  logger.info("StartMatchmaking: Creating new matchmaking ticket...");

  // Build attributes – beacons latency map is the key field for latency-based matchmaking
  const attributes = {};
  let finalBeacons = pings;

  if (finalBeacons && typeof finalBeacons === "string") {
    try {
      finalBeacons = JSON.parse(finalBeacons);
      logger.info("StartMatchmaking: Parsed pings from stringified JSON.");
    } catch (e) {
      logger.error("StartMatchmaking: Failed to parse pings string: " + e.message);
    }
  }

  if (finalBeacons && typeof finalBeacons === "object" && Object.keys(finalBeacons).length > 0) {
    // Edgegap expects: attributes.beacons = { "CityName": ms_float }
    attributes.beacons = finalBeacons;
    logger.info(`StartMatchmaking: Using ${Object.keys(finalBeacons).length} client beacon latencies.`);
  } else {
    logger.info("StartMatchmaking: WARNING: No client ping data provided or empty.");
  }

  // Parse players array
  let finalPlayers = players;
  if (finalPlayers && typeof finalPlayers === "string") {
    try {
      finalPlayers = JSON.parse(finalPlayers);
      logger.info("StartMatchmaking: Parsed players from stringified JSON.");
    } catch (e) {
      logger.error("StartMatchmaking: Failed to parse players string: " + e.message);
    }
  }

  if (Array.isArray(finalPlayers) && finalPlayers.length > 1) {
    // ── MULTI-PLAYER MATCHMAKING (POST /tickets riêng cho từng người, không group_id) ──
    // NOTE: Không dùng group_id vì Edgegap reject format custom ("grp_...") và gây chọn server sai.
    // Edgegap matchmaker tự ghép các solo tickets lại dựa theo beacon/latency.
    logger.info(`StartMatchmaking: Creating ${finalPlayers.length} separate solo tickets (no group_id)...`);
    try {
      const tickets = [];
      for (let i = 0; i < finalPlayers.length; i++) {
        const p = finalPlayers[i];
        const payload = {
          profile: MATCHMAKING_PROFILE,
          attributes: {
            beacons: finalBeacons || {}
          }
        };
        if (playerIp) payload.player_ip = playerIp;

        const response = await axios.post(`${OM_BASE_URL}/tickets`, payload, {
          headers,
          timeout: 10000,
        });
        tickets.push(response.data);
        logger.info(`StartMatchmaking: Created solo ticket for player ${p.username || p.id}: ${response.data.id}`);
      }

      // Encode all ticket IDs separated by semicolon — host polls ticket[0]
      const ticketIdsStr = tickets.map(t => t.id).join(";");
      
      const hostTicket = tickets[0];
      hostTicket.id = ticketIdsStr;
      
      return JSON.stringify(hostTicket);
    } catch (error) {
      const status = error.response ? error.response.status : "N/A";
      const data = error.response ? JSON.stringify(error.response.data) : error.message;
      throw new Error(`Edgegap API multi-tickets error: ${status} - ${data}. Params received: ${JSON.stringify(params || {})}`);
    }
  } else {
    // ── SOLO MATCHMAKING (POST /tickets) ──
    logger.info("StartMatchmaking: Creating individual matchmaking ticket...");
    const body = {
      profile: MATCHMAKING_PROFILE,
      attributes,
    };
    if (playerIp) {
      body.player_ip = playerIp;
    }

    try {
      const response = await axios.post(`${OM_BASE_URL}/tickets`, body, {
        headers,
        timeout: 10000,
      });

      const ticket = response.data;
      logger.info(`StartMatchmaking: Solo ticket created! ID = ${ticket.id}, Status = ${ticket.status}`);

      // Return the ticket – client stores ticket.id and polls with it
      return JSON.stringify(ticket);
    } catch (error) {
      const status = error.response ? error.response.status : "N/A";
      const data = error.response ? JSON.stringify(error.response.data) : error.message;
      throw new Error(`Edgegap API tickets error: ${status} - ${data}. Params received: ${JSON.stringify(params || {})}. Attributes sent: ${JSON.stringify(attributes || {})}`);
    }
  }
};

// Define UGS Cloud Code script parameters so UGS doesn't discard them when sent from the client
module.exports.params = {
  pings: "JSON",
  ticketId: "String",
  cancel: "Boolean",
  players: "JSON",
  playerIp: "String"
};

